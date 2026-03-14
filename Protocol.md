# Board Game Simulator - Communication Protocol

## 1. Overview
- **Transport Layer**: TCP Long Connection
- **Data Format**: Binary Header + JSON Body
- **Byte Order**: Big-Endian (Network Byte Order) MUST be used for all numeric types in the header.

## 2. Packet Structure
Every packet transmitted between the Client and Server consists of an 8-byte Header and a variable-length Body.

| Field    | Size (Bytes) | Type       | Endianness | Description                                       |
| -------- | ------------ | ---------- | ---------- | ------------------------------------------------- |
| `Length` | 4            | `uint32_t` | Big-Endian | The exact byte length of the following JSON body. |
| `MsgID`  | 4            | `uint32_t` | Big-Endian | The unique command ID for routing.                |
| `Body`   | Variable     | `string`   | UTF-8      | JSON formatted payload.                           |

## 3. Message IDs (MsgID)
### Client to Server (CS)
- `1001` : CS_JOIN_ROOM (Payload: { "roomId": 123, "token": "..." })
- `2001` : CS_PLAYER_ACTION (Payload: { "action": "Call", "amount": 20 })

### Server to Client (SC)
- `1002` : SC_JOIN_ROOM_RES (Payload: { "success": true, "message": "OK" })
- `3001` : SC_GAME_SYNC (Payload: { "pot": 100, "currentTurn": 1005, ... })