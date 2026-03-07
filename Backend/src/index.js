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

function buildToken(user) {
  return jwt.sign({ uid: user.id, username: user.username }, JWT_SECRET, {
    expiresIn: JWT_EXPIRES_IN
  });
}

function sanitizeUser(user) {
  return { id: user.id, username: user.username };
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
    const auth = req.headers.authorization || '';
    const token = auth.startsWith('Bearer ') ? auth.substring(7) : '';
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

app.listen(Number(PORT), () => {
  console.log(`Auth server listening on :${PORT}`);
});
