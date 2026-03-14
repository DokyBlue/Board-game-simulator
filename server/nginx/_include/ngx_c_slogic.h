
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
    struct PlayerStats
    {
        std::string username;
        int chips;
        uint64_t userId;

        PlayerStats() : username(""), chips(2000), userId(0) {}
    };

    struct PlayerState
    {
        int chips;
        int currentBet;
        bool isFolded;
        bool isAllIn;
        std::string lastAction;

        PlayerState() : chips(2000), currentBet(0), isFolded(false), isAllIn(false), lastAction("Waiting") {}
    };

    mutable std::shared_mutex roomMutex;
    std::vector<lpngx_connection_t> players;
    lpngx_connection_t owner;
    uint32_t pot;
    uint32_t maxBet;
    std::string stage;
    bool isPlaying;
    uint64_t currentTurnUserId;
    std::vector<std::string> communityCards;
    std::vector<std::string> deck;
    std::unordered_map<lpngx_connection_t,std::vector<std::string>> holeCards;
    std::unordered_map<lpngx_connection_t,PlayerState> playerStates;
    struct BotPlayer
    {
        uint64_t userId;
        std::string username;
        PlayerState state;
        std::vector<std::string> holeCards;
        int style;

        BotPlayer() : userId(0), username(""), state(), holeCards(), style(0) {}
    };

    std::unordered_map<lpngx_connection_t,PlayerStats> playerStats;
    std::vector<BotPlayer> bots;

    GameRoom() : owner(NULL), pot(0), maxBet(0), stage("Waiting"), isPlaying(false), currentTurnUserId(0) {}
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
	bool _HandleResetChips(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);
	bool _HandleGameAction(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);
	bool _HandleLeaveRoom(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength);

	virtual void procPingTimeOutChecking(LPSTRUC_MSG_HEADER tmpmsg,time_t cur_time);      //心跳包检测时间到，该去检测心跳包是否超时的事宜，本函数只是把内存释放，子类应该重新事先该函数以实现具体的判断动作

public:
	virtual void threadRecvProcFunc(char *pMsgBuf);

protected:
	virtual void OnConnectionClosed(lpngx_connection_t pConn) override;

private:
	void SendJsonPkgToClient(LPSTRUC_MSG_HEADER pMsgHeader,unsigned short iMsgCode,const std::string &jsonPayload);
	void BroadcastGameState(uint32_t roomId,const std::shared_ptr<GameRoom> &room);
	void FillBotsForRoom(uint32_t roomId,const std::shared_ptr<GameRoom> &room);
	void AdvanceTurn(const std::shared_ptr<GameRoom> &room,uint64_t currentUserId);
	void RunBotTurns(uint32_t roomId,const std::shared_ptr<GameRoom> &room);

private:
	std::unordered_map<uint32_t,std::shared_ptr<GameRoom>> m_gameRooms;
	std::unordered_map<lpngx_connection_t,uint32_t> m_connRoomMap;
	std::shared_mutex m_roomMapMutex;
	std::unordered_map<uint32_t,bool (CLogicSocket::*)(lpngx_connection_t,LPSTRUC_MSG_HEADER,char *,unsigned short)> m_statusHandler;
};

#endif
