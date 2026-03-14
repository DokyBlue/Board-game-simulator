
#ifndef __NGX_C_SLOGIC_H__
#define __NGX_C_SLOGIC_H__

#include <sys/socket.h>
#include <memory>
#include <shared_mutex>
#include <string>
#include <unordered_map>
#include <vector>
#include "ngx_c_socket.h"

struct GameRoom
{
    mutable std::shared_mutex roomMutex;
    std::vector<lpngx_connection_t> players;
    uint32_t pot;
    std::string stage;
    bool isPlaying;
    uint64_t currentTurnUserId;
    std::vector<std::string> communityCards;
    std::unordered_map<lpngx_connection_t,std::vector<std::string>> holeCards;

    GameRoom() : pot(0), stage("Waiting"), isPlaying(false), currentTurnUserId(0) {}
};

class CLogicSocket : public CSocekt   //继承自父类CScoekt
{
public:
	CLogicSocket();                                                         //构造函数
	virtual ~CLogicSocket();                                                //释放函数
	virtual bool Initialize();                                              //初始化函数

public:

	//通用收发数据相关函数
	void  SendNoBodyPkgToClient(LPSTRUC_MSG_HEADER pMsgHeader,unsigned short iMsgCode);

	//各种业务逻辑相关函数都在之类
	bool _HandleRegister(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);
	bool _HandleLogIn(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);
	bool _HandlePing(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);
	bool _HandleJoinRoom(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);
	bool _HandleStartGame(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);
	bool _HandleGameAction(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);

	virtual void procPingTimeOutChecking(LPSTRUC_MSG_HEADER tmpmsg,time_t cur_time);      //心跳包检测时间到，该去检测心跳包是否超时的事宜，本函数只是把内存释放，子类应该重新事先该函数以实现具体的判断动作

public:
	virtual void threadRecvProcFunc(char *pMsgBuf);

private:
	void SendJsonPkgToClient(LPSTRUC_MSG_HEADER pMsgHeader,unsigned short iMsgCode,const std::string &jsonPayload);
	void BroadcastGameState(uint32_t roomId,const std::shared_ptr<GameRoom> &room);

private:
	std::unordered_map<uint32_t,std::shared_ptr<GameRoom>> m_gameRooms;
	std::unordered_map<lpngx_connection_t,uint32_t> m_connRoomMap;
	std::shared_mutex m_roomMapMutex;
	std::unordered_map<uint32_t,bool (CLogicSocket::*)(lpngx_connection_t,LPSTRUC_MSG_HEADER,char *,unsigned short)> m_statusHandler;
};

#endif
