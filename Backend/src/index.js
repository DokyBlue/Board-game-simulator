import express from 'express';
import cors from 'cors';
import dotenv from 'dotenv';
import jwt from 'jsonwebtoken';
import bcrypt from 'bcryptjs';
import mysql from 'mysql2/promise';

dotenv.config();

const app = express();
app.use(cors());
app.use(express.json());

const {
  PORT = 8080,
  MYSQL_HOST,
  MYSQL_PORT = 3306,
  MYSQL_USER,
  MYSQL_PASSWORD,
  MYSQL_DATABASE,
  JWT_SECRET = 'dev_secret',
  JWT_EXPIRES_IN = '7d'
} = process.env;

const pool = mysql.createPool({
  host: MYSQL_HOST,
  port: Number(MYSQL_PORT),
  user: MYSQL_USER,
  password: MYSQL_PASSWORD,
  database: MYSQL_DATABASE,
  connectionLimit: 10
});

const roomEventStreams = new Map();
const roomRuntimeState = new Map();

function getRoomState(roomId) {
  const normalizedRoomId = Number(roomId);
  if (!roomRuntimeState.has(normalizedRoomId)) {
    roomRuntimeState.set(normalizedRoomId, { started: false, startedAt: null });
  }

  return roomRuntimeState.get(normalizedRoomId);
}

function closeRoomStreams(roomId) {
  const normalizedRoomId = Number(roomId);
  const streams = roomEventStreams.get(normalizedRoomId) || new Set();
  for (const stream of streams) {
    stream.end();
  }

  roomEventStreams.delete(normalizedRoomId);
}

function broadcastRoomEvent(roomId, payload) {
  const normalizedRoomId = Number(roomId);
  const streams = roomEventStreams.get(normalizedRoomId) || new Set();
  const data = `data: ${JSON.stringify(payload)}\n\n`;
  for (const stream of streams) {
    stream.write(data);
  }
}

async function getRoomMembers(roomId) {
  const [members] = await pool.query(
    `SELECT rm.user_id AS userId, u.username, rm.is_owner AS isOwner
     FROM room_members rm
     INNER JOIN users u ON u.id = rm.user_id
     WHERE rm.room_id = ?
     ORDER BY rm.joined_at ASC`,
    [roomId]
  );

  return members;
}

async function broadcastRoomMembers(roomId) {
  const members = await getRoomMembers(roomId);
  broadcastRoomEvent(roomId, {
    type: 'members-updated',
    roomId: Number(roomId),
    members
  });
}

function buildToken(user) {
  return jwt.sign({ uid: user.id, username: user.username }, JWT_SECRET, {
    expiresIn: JWT_EXPIRES_IN
  });
}

function sanitizeUser(user) {
  return { id: user.id, username: user.username };
}

function getBearerToken(req) {
  const auth = req.headers.authorization || '';
  return auth.startsWith('Bearer ') ? auth.substring(7) : '';
}

function authRequired(req, res, next) {
  try {
    const token = getBearerToken(req);
    if (!token) {
      return res.status(401).json({ message: '缺少Token' });
    }

    req.user = jwt.verify(token, JWT_SECRET);
    return next();
  } catch (error) {
    return res.status(401).json({ message: 'Token无效', detail: error.message });
  }
}

function buildRoomCode() {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  let code = '';
  for (let i = 0; i < 6; i += 1) {
    code += chars.charAt(Math.floor(Math.random() * chars.length));
  }

  return code;
}

app.get('/health', async (_, res) => {
  try {
    await pool.query('SELECT 1');
    res.json({ status: 'ok' });
  } catch (error) {
    res.status(500).json({ status: 'db_error', message: error.message });
  }
});

app.post('/auth/register', async (req, res) => {
  const username = (req.body.username || '').trim();
  const password = req.body.password || '';

  if (!username || !password) {
    return res.status(400).json({ message: '用户名和密码不能为空' });
  }

  if (password.length < 6) {
    return res.status(400).json({ message: '密码长度至少为6位' });
  }

  try {
    const [existing] = await pool.query('SELECT id FROM users WHERE username = ?', [username]);
    if (existing.length > 0) {
      return res.status(409).json({ message: '用户名已存在' });
    }

    const passwordHash = await bcrypt.hash(password, 10);
    const [result] = await pool.query(
      'INSERT INTO users(username, password_hash) VALUES(?, ?)',
      [username, passwordHash]
    );

    const user = { id: result.insertId, username };
    const token = buildToken(user);
    return res.status(201).json({ token, user: sanitizeUser(user) });
  } catch (error) {
    return res.status(500).json({ message: '注册失败', detail: error.message });
  }
});

app.post('/auth/login', async (req, res) => {
  const username = (req.body.username || '').trim();
  const password = req.body.password || '';

  if (!username || !password) {
    return res.status(400).json({ message: '用户名和密码不能为空' });
  }

  try {
    const [rows] = await pool.query('SELECT id, username, password_hash FROM users WHERE username = ?', [username]);
    if (rows.length === 0) {
      return res.status(401).json({ message: '用户名或密码错误' });
    }

    const user = rows[0];
    const matched = await bcrypt.compare(password, user.password_hash);
    if (!matched) {
      return res.status(401).json({ message: '用户名或密码错误' });
    }

    const token = buildToken(user);
    return res.json({ token, user: sanitizeUser(user) });
  } catch (error) {
    return res.status(500).json({ message: '登录失败', detail: error.message });
  }
});

app.get('/auth/me', async (req, res) => {
  try {
    const token = getBearerToken(req);
    if (!token) {
      return res.status(401).json({ message: '缺少Token' });
    }

    const payload = jwt.verify(token, JWT_SECRET);
    const [rows] = await pool.query('SELECT id, username FROM users WHERE id = ?', [payload.uid]);
    if (rows.length === 0) {
      return res.status(401).json({ message: '用户不存在' });
    }

    return res.json({ user: rows[0] });
  } catch (error) {
    return res.status(401).json({ message: 'Token无效', detail: error.message });
  }
});

app.post('/lobby/rooms', authRequired, async (req, res) => {
  const gameKey = (req.body.gameKey || '').trim();
  if (!gameKey) {
    return res.status(400).json({ message: 'gameKey不能为空' });
  }

  const ownerId = req.user.uid;
  const ownerName = req.user.username;

  try {
    let code = '';
    for (let i = 0; i < 8; i += 1) {
      code = buildRoomCode();
      const [exists] = await pool.query('SELECT id FROM game_rooms WHERE code = ? LIMIT 1', [code]);
      if (exists.length === 0) {
        break;
      }
    }

    const [created] = await pool.query(
      'INSERT INTO game_rooms(code, game_key, owner_user_id) VALUES(?, ?, ?)',
      [code, gameKey, ownerId]
    );

    await pool.query(
      'INSERT INTO room_members(room_id, user_id, is_owner) VALUES(?, ?, 1)',
      [created.insertId, ownerId]
    );

    return res.status(201).json({
      room: {
        id: created.insertId,
        code,
        gameKey,
        owner: ownerName,
        ownerUserId: ownerId
      }
    });
  } catch (error) {
    return res.status(500).json({ message: '创建房间失败', detail: error.message });
  }
});

app.post('/lobby/rooms/join', authRequired, async (req, res) => {
  const code = (req.body.code || '').trim().toUpperCase();
  const userId = req.user.uid;
  const username = req.user.username;

  if (!code) {
    return res.status(400).json({ message: '房间号不能为空' });
  }

  try {
    const [rooms] = await pool.query('SELECT id, code, game_key, owner_user_id FROM game_rooms WHERE code = ? LIMIT 1', [code]);
    if (rooms.length === 0) {
      return res.status(404).json({ message: '房间不存在' });
    }

    const room = rooms[0];
    const [members] = await pool.query('SELECT id FROM room_members WHERE room_id = ? AND user_id = ? LIMIT 1', [room.id, userId]);
    let isNewMember = false;
    if (members.length === 0) {
      const [memberCountRows] = await pool.query('SELECT COUNT(1) AS count FROM room_members WHERE room_id = ?', [room.id]);
      const memberCount = Number(memberCountRows[0]?.count || 0);
      if (memberCount >= 6) {
        return res.status(409).json({ message: '房间人数已满（最多6人）' });
      }

      await pool.query('INSERT INTO room_members(room_id, user_id, is_owner) VALUES(?, ?, 0)', [room.id, userId]);
      isNewMember = true;
    }

    if (isNewMember) {
      await broadcastRoomMembers(room.id);
    }

    return res.json({
      room: {
        id: room.id,
        code: room.code,
        gameKey: room.game_key,
        ownerUserId: room.owner_user_id,
        joinedUser: username
      }
    });
  } catch (error) {
    return res.status(500).json({ message: '加入房间失败', detail: error.message });
  }
});

app.get('/lobby/rooms/:roomId/members', authRequired, async (req, res) => {
  const roomId = Number(req.params.roomId || 0);
  const userId = Number(req.user.uid || 0);

  if (!roomId) {
    return res.status(400).json({ message: 'roomId不能为空' });
  }

  try {
    const [membershipRows] = await pool.query(
      'SELECT id FROM room_members WHERE room_id = ? AND user_id = ? LIMIT 1',
      [roomId, userId]
    );

    if (membershipRows.length === 0) {
      return res.status(403).json({ message: '仅房间成员可查看成员列表' });
    }

    const members = await getRoomMembers(roomId);

    return res.json({
      roomId,
      members
    });
  } catch (error) {
    return res.status(500).json({ message: '获取房间成员失败', detail: error.message });
  }
});


app.post('/lobby/rooms/leave', authRequired, async (req, res) => {
  const roomId = Number(req.body.roomId || 0);
  const userId = req.user.uid;

  if (!roomId) {
    return res.status(400).json({ message: 'roomId不能为空' });
  }

  try {
    const [rooms] = await pool.query('SELECT id, owner_user_id FROM game_rooms WHERE id = ? LIMIT 1', [roomId]);
    if (rooms.length === 0) {
      return res.status(404).json({ message: '房间不存在' });
    }

    const room = rooms[0];
    if (Number(room.owner_user_id) === Number(userId)) {
      await pool.query('DELETE FROM game_rooms WHERE id = ?', [roomId]);
      closeRoomStreams(roomId);
      roomRuntimeState.delete(Number(roomId));
      return res.json({ message: '房主已离开，房间已销毁' });
    }

    await pool.query('DELETE FROM room_members WHERE room_id = ? AND user_id = ?', [roomId, userId]);
    const [members] = await pool.query('SELECT id FROM room_members WHERE room_id = ? LIMIT 1', [roomId]);
    if (members.length === 0) {
      await pool.query('DELETE FROM game_rooms WHERE id = ?', [roomId]);
      closeRoomStreams(roomId);
      roomRuntimeState.delete(Number(roomId));
      return res.json({ message: '房间无人，已自动销毁' });
    }

    await broadcastRoomMembers(roomId);

    return res.json({ message: '已退出房间' });
  } catch (error) {
    return res.status(500).json({ message: '退出房间失败', detail: error.message });
  }
});

app.get('/lobby/rooms/:roomId/events', authRequired, async (req, res) => {
  const roomId = Number(req.params.roomId || 0);
  const userId = Number(req.user.uid || 0);

  if (!roomId) {
    return res.status(400).json({ message: 'roomId不能为空' });
  }

  try {
    const [membershipRows] = await pool.query(
      'SELECT id FROM room_members WHERE room_id = ? AND user_id = ? LIMIT 1',
      [roomId, userId]
    );

    if (membershipRows.length === 0) {
      return res.status(403).json({ message: '仅房间成员可订阅房间事件' });
    }

    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');
    res.flushHeaders();

    if (!roomEventStreams.has(roomId)) {
      roomEventStreams.set(roomId, new Set());
    }

    roomEventStreams.get(roomId).add(res);

    const members = await getRoomMembers(roomId);
    const roomState = getRoomState(roomId);
    res.write(
      `data: ${JSON.stringify({
        type: 'room-snapshot',
        roomId,
        members,
        gameStarted: roomState.started,
        startedAt: roomState.startedAt
      })}\n\n`
    );

    const heartbeat = setInterval(() => {
      res.write(': heartbeat\n\n');
    }, 20000);

    req.on('close', () => {
      clearInterval(heartbeat);
      const roomStreams = roomEventStreams.get(roomId);
      if (!roomStreams) {
        return;
      }

      roomStreams.delete(res);
      if (roomStreams.size === 0) {
        roomEventStreams.delete(roomId);
      }
    });
  } catch (error) {
    return res.status(500).json({ message: '订阅房间事件失败', detail: error.message });
  }
});

app.post('/lobby/rooms/:roomId/start', authRequired, async (req, res) => {
  const roomId = Number(req.params.roomId || 0);
  const userId = Number(req.user.uid || 0);

  if (!roomId) {
    return res.status(400).json({ message: 'roomId不能为空' });
  }

  try {
    const [rooms] = await pool.query('SELECT id, owner_user_id FROM game_rooms WHERE id = ? LIMIT 1', [roomId]);
    if (rooms.length === 0) {
      return res.status(404).json({ message: '房间不存在' });
    }

    const room = rooms[0];
    if (Number(room.owner_user_id) !== userId) {
      return res.status(403).json({ message: '仅房主可开始游戏' });
    }

    const roomState = getRoomState(roomId);
    if (!roomState.started) {
      roomState.started = true;
      roomState.startedAt = new Date().toISOString();
    }

    broadcastRoomEvent(roomId, {
      type: 'game-started',
      roomId,
      startedAt: roomState.startedAt,
      startedBy: userId
    });

    return res.json({
      message: '游戏已开始',
      roomId,
      startedAt: roomState.startedAt
    });
  } catch (error) {
    return res.status(500).json({ message: '开始游戏失败', detail: error.message });
  }
});

app.listen(Number(PORT), () => {
  console.log(`Auth server listening on :${PORT}`);
});
