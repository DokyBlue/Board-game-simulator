#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>    //uintptr_t
#include <stdarg.h>    //va_start....
#include <unistd.h>    //STDERR_FILENO等
#include <sys/time.h>  //gettimeofday
#include <time.h>      //localtime_r
#include <fcntl.h>     //open
#include <errno.h>     //errno
//#include <sys/socket.h>
#include <sys/ioctl.h> //ioctl
#include <arpa/inet.h>
#include <pthread.h>   //多线程

#include <algorithm>
#include <random>
#include <sstream>
#include <mutex>
#include <cctype>

#include <nlohmann/json.hpp>

#include "ngx_c_conf.h"
#include "ngx_macro.h"
#include "ngx_global.h"
#include "ngx_func.h"
//#include "ngx_c_socket.h"
#include "ngx_c_memory.h"
#include "ngx_c_slogic.h"
#include "ngx_logiccomm.h"
#include "ngx_c_lockmutex.h"

namespace
{
std::string ToLower(const std::string &text)
{
    std::string lowered = text;
    std::transform(lowered.begin(),lowered.end(),lowered.begin(),[](unsigned char c){ return static_cast<char>(std::tolower(c)); });
    return lowered;
}

void EncodeCard(const std::string &cardCode,std::string &rank,std::string &suit)
{
    rank = "Two";
    suit = "Spades";

    if(cardCode.size() < 2)
    {
        return;
    }

    std::string suitPart;
    char rankCode = cardCode[0];

    if(cardCode.size() >= 2 && cardCode[1] == '0')
    {
        rankCode = 'T';
        suitPart = cardCode.substr(2);
    }
    else
    {
        suitPart = cardCode.substr(1);
    }

    if(rankCode == '2') rank = "Two";
    else if(rankCode == '3') rank = "Three";
    else if(rankCode == '4') rank = "Four";
    else if(rankCode == '5') rank = "Five";
    else if(rankCode == '6') rank = "Six";
    else if(rankCode == '7') rank = "Seven";
    else if(rankCode == '8') rank = "Eight";
    else if(rankCode == '9') rank = "Nine";
    else if(rankCode == 'T') rank = "Ten";
    else if(rankCode == 'J') rank = "Jack";
    else if(rankCode == 'Q') rank = "Queen";
    else if(rankCode == 'K') rank = "King";
    else if(rankCode == 'A') rank = "Ace";

    if(suitPart == "Clubs") suit = "Clubs";
    else if(suitPart == "Diamonds") suit = "Diamonds";
    else if(suitPart == "Hearts") suit = "Hearts";
    else if(suitPart == "Spades") suit = "Spades";
}

bool IsActivePlayer(const GameRoom::PlayerState &state)
{
    return !state.isFolded && !state.isAllIn;
}

uint64_t GetPlayerUserId(const std::shared_ptr<GameRoom> &room,lpngx_connection_t playerConn)
{
    if(room == NULL || playerConn == NULL)
    {
        return 0;
    }

    std::unordered_map<lpngx_connection_t,GameRoom::PlayerStats>::const_iterator statsIt = room->playerStats.find(playerConn);
    if(statsIt != room->playerStats.end() && statsIt->second.userId != 0)
    {
        return statsIt->second.userId;
    }

    return static_cast<uint64_t>(reinterpret_cast<uintptr_t>(playerConn));
}


int EstimateHoleStrength(const std::vector<std::string> &holeCards)
{
    if(holeCards.size() < 2)
    {
        return 1;
    }

    auto rankValue = [](const std::string &card)->int
    {
        if(card.empty()) return 2;
        char r = card[0];
        if(r >= '2' && r <= '9') return r - '0';
        if(r == 'T') return 10;
        if(r == 'J') return 11;
        if(r == 'Q') return 12;
        if(r == 'K') return 13;
        if(r == 'A') return 14;
        return 2;
    };

    int r1 = rankValue(holeCards[0]);
    int r2 = rankValue(holeCards[1]);
    int high = std::max(r1,r2);

    if(r1 == r2 && high >= 10) return 4;
    if(r1 == r2) return 3;
    if(high >= 13) return 3;
    if(high >= 10) return 2;
    return 1;
}

int ComputeBotRaise(const GameRoom::BotPlayer &bot,uint32_t maxBet)
{
    int strength = EstimateHoleStrength(bot.holeCards);
    int toCall = static_cast<int>(maxBet) - bot.state.currentBet;
    if(toCall < 0) toCall = 0;

    int base = 20;
    if(bot.style == 2) base = 40;
    else if(bot.style == 3) base = 30;

    return std::max(toCall + base * strength, toCall);
}
} // namespace

std::vector<std::string> GenerateShuffledDeck()
{
    static const char *suits[] = {"Hearts","Diamonds","Clubs","Spades"};
    static const char *ranks[] = {"2","3","4","5","6","7","8","9","T","J","Q","K","A"};

    std::vector<std::string> deck;
    deck.reserve(52);

    for(std::size_t i = 0; i < sizeof(suits) / sizeof(suits[0]); ++i)
    {
        for(std::size_t j = 0; j < sizeof(ranks) / sizeof(ranks[0]); ++j)
        {
            deck.push_back(std::string(ranks[j]) + suits[i]);
        }
    }

    std::random_device rd;
    std::mt19937 g(rd());
    std::shuffle(deck.begin(),deck.end(),g);
    return deck;
}

//构造函数
CLogicSocket::CLogicSocket()
{

}
//析构函数
CLogicSocket::~CLogicSocket()
{

}

//初始化函数【fork()子进程之前干这个事】
//成功返回true，失败返回false
bool CLogicSocket::Initialize()
{
    m_statusHandler.clear();

    //兼容历史命令
    m_statusHandler[_CMD_PING] = &CLogicSocket::_HandlePing;
    m_statusHandler[_CMD_REGISTER] = &CLogicSocket::_HandleRegister;
    m_statusHandler[_CMD_LOGIN] = &CLogicSocket::_HandleLogIn;

    //新增德州扑克相关命令
    m_statusHandler[1001] = &CLogicSocket::_HandleJoinRoom;
    m_statusHandler[2002] = &CLogicSocket::_HandleStartGame;
    m_statusHandler[2001] = &CLogicSocket::_HandleGameAction;
    m_statusHandler[2003] = &CLogicSocket::_HandleLeaveRoom;
    m_statusHandler[2004] = &CLogicSocket::_HandleResetChips;

    bool bParentInit = CSocekt::Initialize();  //调用父类的同名函数
    return bParentInit;
}

//处理收到的数据包，由线程池来调用本函数，本函数是一个单独的线程；
void CLogicSocket::threadRecvProcFunc(char *pMsgBuf)
{
    LPSTRUC_MSG_HEADER pMsgHeader = (LPSTRUC_MSG_HEADER)pMsgBuf;                  //消息头
    LPCOMM_PKG_HEADER  pPkgHeader = (LPCOMM_PKG_HEADER)(pMsgBuf+m_iLenMsgHeader); //包头
    void  *pPkgBody;                                                              //指向包体的指针
    uint32_t pkglen = pPkgHeader->pkgLen;                            //客户端指明的包宽度【包头+包体】

    if(m_iLenPkgHeader == pkglen)
    {
        //没有包体，只有包头
		pPkgBody = NULL;
    }
    else
	{
        //有包体，走到这里
		pPkgBody = (void *)(pMsgBuf+m_iLenMsgHeader+m_iLenPkgHeader); //跳过消息头 以及 包头 ，指向包体
	}

    uint32_t imsgCode = pPkgHeader->msgCode; //消息代码拿出来
    lpngx_connection_t p_Conn = pMsgHeader->pConn;        //消息头中藏着连接池中连接的指针

    if(p_Conn->iCurrsequence != pMsgHeader->iCurrsequence)
    {
        return; //丢弃不理这种包了【客户端断开了】
    }

    std::unordered_map<uint32_t,bool (CLogicSocket::*)(lpngx_connection_t,LPSTRUC_MSG_HEADER,char *,unsigned short)>::const_iterator it = m_statusHandler.find(imsgCode);
    if(it == m_statusHandler.end() || it->second == NULL)
    {
        ngx_log_stderr(0,"CLogicSocket::threadRecvProcFunc()中imsgCode=%d消息码找不到对应的处理函数!",imsgCode);
        return;
    }

    (this->*(it->second))(p_Conn,pMsgHeader,(char *)pPkgBody,pkglen-m_iLenPkgHeader);
    return;
}

//心跳包检测时间到，该去检测心跳包是否超时的事宜，本函数是子类函数，实现具体的判断动作
void CLogicSocket::procPingTimeOutChecking(LPSTRUC_MSG_HEADER tmpmsg,time_t cur_time)
{
    CMemory *p_memory = CMemory::GetInstance();

    if(tmpmsg->iCurrsequence == tmpmsg->pConn->iCurrsequence) //此连接没断
    {
        lpngx_connection_t p_Conn = tmpmsg->pConn;

        if(/*m_ifkickTimeCount == 1 && */m_ifTimeOutKick == 1)  //能调用到本函数第一个条件肯定成立，所以第一个条件加不加无所谓，主要是第二个条件
        {
            zdClosesocketProc(p_Conn);
        }
        else if( (cur_time - p_Conn->lastPingTime ) > (m_iWaitTime*3+10) ) //超时踢的判断标准就是 每次检查的时间间隔*3，超过这个时间没发送心跳包，就踢【大家可以根据实际情况自由设定】
        {
            zdClosesocketProc(p_Conn);
        }

        p_memory->FreeMemory(tmpmsg);//内存要释放
    }
    else //此连接断了
    {
        p_memory->FreeMemory(tmpmsg);//内存要释放
    }
    return;
}

//发送没有包体的数据包给客户端
void CLogicSocket::SendNoBodyPkgToClient(LPSTRUC_MSG_HEADER pMsgHeader,unsigned short iMsgCode)
{
    CMemory  *p_memory = CMemory::GetInstance();

    char *p_sendbuf = (char *)p_memory->AllocMemory(m_iLenMsgHeader+m_iLenPkgHeader,false);
    char *p_tmpbuf = p_sendbuf;

	memcpy(p_tmpbuf,pMsgHeader,m_iLenMsgHeader);
	p_tmpbuf += m_iLenMsgHeader;

    LPCOMM_PKG_HEADER pPkgHeader = (LPCOMM_PKG_HEADER)p_tmpbuf;
    pPkgHeader->msgCode = iMsgCode;
    pPkgHeader->pkgLen = m_iLenPkgHeader;
    msgSend(p_sendbuf);
    return;
}

void CLogicSocket::SendJsonPkgToClient(LPSTRUC_MSG_HEADER pMsgHeader,unsigned short iMsgCode,const std::string &jsonPayload)
{
    CMemory  *p_memory = CMemory::GetInstance();

    int iSendLen = static_cast<int>(jsonPayload.size());
    char *p_sendbuf = (char *)p_memory->AllocMemory(m_iLenMsgHeader+m_iLenPkgHeader+iSendLen,false);

    memcpy(p_sendbuf,pMsgHeader,m_iLenMsgHeader);

    LPCOMM_PKG_HEADER pPkgHeader = (LPCOMM_PKG_HEADER)(p_sendbuf+m_iLenMsgHeader);
    pPkgHeader->msgCode = iMsgCode;
    pPkgHeader->pkgLen = m_iLenPkgHeader + iSendLen;

    if(iSendLen > 0)
    {
        memcpy(p_sendbuf+m_iLenMsgHeader+m_iLenPkgHeader,jsonPayload.data(),iSendLen);
    }

    msgSend(p_sendbuf);
}

//----------------------------------------------------------------------------------------------------------
//处理各种业务逻辑
bool CLogicSocket::_HandleRegister(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    if(pPkgBody == NULL)
    {
        return false;
    }

    int iRecvLen = sizeof(STRUCT_REGISTER);
    if(iRecvLen != iBodyLength)
    {
        return false;
    }

    CLock lock(&pConn->logicPorcMutex);

    LPSTRUCT_REGISTER p_RecvInfo = (LPSTRUCT_REGISTER)pPkgBody;
    p_RecvInfo->iType = ntohl(p_RecvInfo->iType);
    p_RecvInfo->username[sizeof(p_RecvInfo->username)-1]=0;
    p_RecvInfo->password[sizeof(p_RecvInfo->password)-1]=0;

    LPCOMM_PKG_HEADER pPkgHeader;
    CMemory  *p_memory = CMemory::GetInstance();
    int iSendLen = sizeof(STRUCT_REGISTER);

    char *p_sendbuf = (char *)p_memory->AllocMemory(m_iLenMsgHeader+m_iLenPkgHeader+iSendLen,false);
    memcpy(p_sendbuf,pMsgHeader,m_iLenMsgHeader);
    pPkgHeader = (LPCOMM_PKG_HEADER)(p_sendbuf+m_iLenMsgHeader);
    pPkgHeader->msgCode = _CMD_REGISTER;
    pPkgHeader->pkgLen  = m_iLenPkgHeader + iSendLen;
    LPSTRUCT_REGISTER p_sendInfo = (LPSTRUCT_REGISTER)(p_sendbuf+m_iLenMsgHeader+m_iLenPkgHeader);
    (void)p_sendInfo;

    msgSend(p_sendbuf);

    return true;
}

bool CLogicSocket::_HandleLogIn(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    if(pPkgBody == NULL)
    {
        return false;
    }
    int iRecvLen = sizeof(STRUCT_LOGIN);
    if(iRecvLen != iBodyLength)
    {
        return false;
    }
    CLock lock(&pConn->logicPorcMutex);

    LPSTRUCT_LOGIN p_RecvInfo = (LPSTRUCT_LOGIN)pPkgBody;
    p_RecvInfo->username[sizeof(p_RecvInfo->username)-1]=0;
    p_RecvInfo->password[sizeof(p_RecvInfo->password)-1]=0;

    LPCOMM_PKG_HEADER pPkgHeader;
    CMemory  *p_memory = CMemory::GetInstance();

    int iSendLen = sizeof(STRUCT_LOGIN);
    char *p_sendbuf = (char *)p_memory->AllocMemory(m_iLenMsgHeader+m_iLenPkgHeader+iSendLen,false);
    memcpy(p_sendbuf,pMsgHeader,m_iLenMsgHeader);
    pPkgHeader = (LPCOMM_PKG_HEADER)(p_sendbuf+m_iLenMsgHeader);
    pPkgHeader->msgCode = _CMD_LOGIN;
    pPkgHeader->pkgLen  = m_iLenPkgHeader + iSendLen;
    LPSTRUCT_LOGIN p_sendInfo = (LPSTRUCT_LOGIN)(p_sendbuf+m_iLenMsgHeader+m_iLenPkgHeader);
    (void)p_sendInfo;

    msgSend(p_sendbuf);
    return true;
}

bool CLogicSocket::_HandlePing(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    if(iBodyLength != 0)
		return false;

    CLock lock(&pConn->logicPorcMutex);
    pConn->lastPingTime = time(NULL);

    SendNoBodyPkgToClient(pMsgHeader,_CMD_PING);

    return true;
}

bool CLogicSocket::_HandleJoinRoom(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    if(pPkgBody == NULL || iBodyLength == 0)
    {
        return false;
    }

    uint32_t roomId = 0;
    std::string username;
    try
    {
        nlohmann::json body = nlohmann::json::parse(std::string(pPkgBody,iBodyLength));
        roomId = body.at("roomId").get<uint32_t>();
        username = body.value<std::string>("username",std::string(""));
    }
    catch(...)
    {
        return false;
    }

    std::shared_ptr<GameRoom> room;
    {
        std::unique_lock<std::shared_mutex> roomMapLock(m_roomMapMutex);
        std::unordered_map<uint32_t,std::shared_ptr<GameRoom>>::iterator roomIt = m_gameRooms.find(roomId);
        if(roomIt == m_gameRooms.end())
        {
            room = std::make_shared<GameRoom>();
            m_gameRooms[roomId] = room;
        }
        else
        {
            room = roomIt->second;
        }

        m_connRoomMap[pConn] = roomId;
    }

    std::vector<lpngx_connection_t> playersSnapshot;
    std::string joinBroadcastJson;
    {
        std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);

        std::vector<lpngx_connection_t>::iterator existingPlayer = std::find(room->players.begin(),room->players.end(),pConn);
        if(existingPlayer == room->players.end())
        {
            if(room->players.size() >= 6)
            {
                SendJsonPkgToClient(pMsgHeader,1001,"{\"status\":\"full\",\"roomId\":" + std::to_string(roomId) + "}");
                return true;
            }

            room->players.push_back(pConn);
            if(room->owner == NULL)
            {
                room->owner = pConn;
            }
            room->playerStates[pConn] = GameRoom::PlayerState();
        }

        std::unordered_map<lpngx_connection_t,GameRoom::PlayerStats>::iterator statsIt = room->playerStats.find(pConn);
        if(statsIt == room->playerStats.end())
        {
            GameRoom::PlayerStats stats;
            stats.userId = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(pConn));
            stats.username = username.empty() ? ("Player-" + std::to_string(stats.userId)) : username;

            std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::const_iterator stateIt = room->playerStates.find(pConn);
            if(stateIt != room->playerStates.end())
            {
                stats.chips = stateIt->second.chips;
            }

            room->playerStats[pConn] = stats;
        }
        else
        {
            if(!username.empty())
            {
                statsIt->second.username = username;
            }

            std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::const_iterator stateIt = room->playerStates.find(pConn);
            if(stateIt != room->playerStates.end())
            {
                statsIt->second.chips = stateIt->second.chips;
            }
        }

        nlohmann::json res;
        res["status"] = "ok";
        res["roomId"] = roomId;
        res["players"] = nlohmann::json::array();

        for(std::size_t i = 0; i < room->players.size(); ++i)
        {
            lpngx_connection_t playerConn = room->players[i];
            if(playerConn == NULL)
            {
                continue;
            }

            std::unordered_map<lpngx_connection_t,GameRoom::PlayerStats>::iterator playerStatsIt = room->playerStats.find(playerConn);
            if(playerStatsIt == room->playerStats.end())
            {
                GameRoom::PlayerStats fallbackStats;
                fallbackStats.userId = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(playerConn));
                fallbackStats.username = "Player-" + std::to_string(fallbackStats.userId);

                std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::const_iterator stateIt = room->playerStates.find(playerConn);
                if(stateIt != room->playerStates.end())
                {
                    fallbackStats.chips = stateIt->second.chips;
                }

                room->playerStats[playerConn] = fallbackStats;
                playerStatsIt = room->playerStats.find(playerConn);
            }

            int chips = playerStatsIt->second.chips;
            std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::const_iterator stateIt = room->playerStates.find(playerConn);
            if(stateIt != room->playerStates.end())
            {
                chips = stateIt->second.chips;
                playerStatsIt->second.chips = chips;
            }

            nlohmann::json playerJson;
            playerJson["username"] = playerStatsIt->second.username;
            playerJson["chips"] = chips;
            playerJson["userId"] = playerStatsIt->second.userId;
            playerJson["isOwner"] = (room->owner == playerConn);
            res["players"].push_back(playerJson);
        }

        joinBroadcastJson = res.dump();
        playersSnapshot = room->players;
    }

    for(std::size_t i = 0; i < playersSnapshot.size(); ++i)
    {
        lpngx_connection_t playerConn = playersSnapshot[i];
        if(playerConn == NULL)
        {
            continue;
        }

        STRUC_MSG_HEADER msgHeader;
        msgHeader.pConn = playerConn;
        msgHeader.iCurrsequence = playerConn->iCurrsequence;
        SendJsonPkgToClient(&msgHeader,1001,joinBroadcastJson);
    }

    return true;
}


bool CLogicSocket::_HandleGameAction(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    (void)pMsgHeader;

    if(pPkgBody == NULL || iBodyLength == 0)
    {
        return false;
    }

    std::string action;
    int amount = 0;
    try
    {
        nlohmann::json body = nlohmann::json::parse(std::string(pPkgBody,iBodyLength));
        action = body.at("action").get<std::string>();
        amount = body.value("amount",0);
    }
    catch(...)
    {
        return false;
    }

    uint32_t roomId = 0;
    {
        std::shared_lock<std::shared_mutex> roomMapReadLock(m_roomMapMutex);
        std::unordered_map<lpngx_connection_t,uint32_t>::const_iterator connIt = m_connRoomMap.find(pConn);
        if(connIt == m_connRoomMap.end())
        {
            return false;
        }
        roomId = connIt->second;
    }

    std::shared_ptr<GameRoom> room;
    {
        std::shared_lock<std::shared_mutex> roomMapReadLock(m_roomMapMutex);
        std::unordered_map<uint32_t,std::shared_ptr<GameRoom>>::const_iterator roomIt = m_gameRooms.find(roomId);
        if(roomIt == m_gameRooms.end())
        {
            return false;
        }
        room = roomIt->second;
    }

    {
        std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);
        std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::iterator playerIt = room->playerStates.find(pConn);
        if(playerIt == room->playerStates.end())
        {
            return false;
        }

        GameRoom::PlayerState &player = playerIt->second;
        const std::string normalizedAction = ToLower(action);

        if(normalizedAction == "call" || normalizedAction == "check")
        {
            int required = static_cast<int>(room->maxBet) - player.currentBet;
            if(required < 0)
            {
                required = 0;
            }

            int paid = std::min(player.chips,required);
            player.chips -= paid;
            player.currentBet += paid;
            room->pot += static_cast<uint32_t>(paid);
            player.lastAction = (normalizedAction == "check" ? "Check" : "Call");

            if(player.chips == 0)
            {
                player.isAllIn = true;
            }
        }
        else if(normalizedAction == "raise")
        {
            int raiseAmount = std::max(0,amount);
            int paid = std::min(player.chips,raiseAmount);
            player.chips -= paid;
            player.currentBet += paid;
            room->pot += static_cast<uint32_t>(paid);
            if(static_cast<uint32_t>(player.currentBet) > room->maxBet)
            {
                room->maxBet = static_cast<uint32_t>(player.currentBet);
            }
            player.lastAction = "Raise";

            if(player.chips == 0)
            {
                player.isAllIn = true;
            }
        }
        else if(normalizedAction == "allin" || normalizedAction == "all_in" || normalizedAction == "all in")
        {
            int paid = player.chips;
            player.chips = 0;
            player.currentBet += paid;
            room->pot += static_cast<uint32_t>(paid);
            player.isAllIn = true;
            player.lastAction = "AllIn";

            if(static_cast<uint32_t>(player.currentBet) > room->maxBet)
            {
                room->maxBet = static_cast<uint32_t>(player.currentBet);
            }
        }
        else if(normalizedAction == "fold")
        {
            player.isFolded = true;
            player.lastAction = "Fold";
        }

        AdvanceTurn(room,GetPlayerUserId(room,pConn));
    }

    BroadcastGameState(roomId,room);
    RunBotTurns(roomId,room);
    return true;
}

bool CLogicSocket::_HandleStartGame(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    (void)pMsgHeader;
    (void)pPkgBody;
    (void)iBodyLength;

    uint32_t roomId = 0;
    {
        std::shared_lock<std::shared_mutex> roomMapReadLock(m_roomMapMutex);
        std::unordered_map<lpngx_connection_t,uint32_t>::const_iterator connIt = m_connRoomMap.find(pConn);
        if(connIt == m_connRoomMap.end())
        {
            return false;
        }
        roomId = connIt->second;
    }

    std::shared_ptr<GameRoom> room;
    {
        std::shared_lock<std::shared_mutex> roomMapReadLock(m_roomMapMutex);
        std::unordered_map<uint32_t,std::shared_ptr<GameRoom>>::const_iterator roomIt = m_gameRooms.find(roomId);
        if(roomIt == m_gameRooms.end())
        {
            return false;
        }
        room = roomIt->second;
    }

    {
        std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);
        if(room->owner != pConn)
        {
            // 非房主无权开局，仍广播当前状态以保持客户端同步
            roomLock.unlock();
            BroadcastGameState(roomId,room);
            return false;
        }

        room->deck = GenerateShuffledDeck();

        room->isPlaying = true;
        room->pot = 0;
        room->maxBet = 0;
        room->stage = "Preflop";
        room->communityCards.clear();
        room->holeCards.clear();

        for(std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::iterator it = room->playerStates.begin(); it != room->playerStates.end(); ++it)
        {
            it->second.currentBet = 0;
            it->second.isFolded = false;
            it->second.isAllIn = false;
            it->second.lastAction = "Waiting";
        }

        for(std::size_t i = 0; i < room->players.size(); ++i)
        {
            lpngx_connection_t playerConn = room->players[i];
            if(playerConn == NULL || room->deck.size() < 2)
            {
                continue;
            }

            std::vector<std::string> cards;
            cards.push_back(room->deck.back());
            room->deck.pop_back();
            cards.push_back(room->deck.back());
            room->deck.pop_back();

            room->holeCards[playerConn] = cards;
        }

        FillBotsForRoom(roomId,room);
        for(std::size_t i = 0; i < room->bots.size(); ++i)
        {
            if(room->deck.size() < 2)
            {
                break;
            }

            room->bots[i].state.currentBet = 0;
            room->bots[i].state.isFolded = false;
            room->bots[i].state.isAllIn = false;
            room->bots[i].state.lastAction = "Waiting";
            room->bots[i].holeCards.clear();
            room->bots[i].holeCards.push_back(deck.back());
            deck.pop_back();
            room->bots[i].holeCards.push_back(deck.back());
            deck.pop_back();
        }

        if(!room->players.empty() && room->players[0] != NULL)
        {
            room->currentTurnUserId = GetPlayerUserId(room,room->players[0]);
        }
        else if(!room->bots.empty())
        {
            room->currentTurnUserId = room->bots[0].userId;
        }
        else
        {
            room->currentTurnUserId = 0;
        }
    }

    BroadcastGameState(roomId,room);
    RunBotTurns(roomId,room);
    return true;
}

bool CLogicSocket::_HandleResetChips(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    (void)pMsgHeader;
    (void)pPkgBody;
    (void)iBodyLength;

    uint32_t roomId = 0;
    {
        std::shared_lock<std::shared_mutex> roomMapReadLock(m_roomMapMutex);
        std::unordered_map<lpngx_connection_t,uint32_t>::const_iterator connIt = m_connRoomMap.find(pConn);
        if(connIt == m_connRoomMap.end())
        {
            return false;
        }
        roomId = connIt->second;
    }

    std::shared_ptr<GameRoom> room;
    {
        std::shared_lock<std::shared_mutex> roomMapReadLock(m_roomMapMutex);
        std::unordered_map<uint32_t,std::shared_ptr<GameRoom>>::const_iterator roomIt = m_gameRooms.find(roomId);
        if(roomIt == m_gameRooms.end())
        {
            return false;
        }
        room = roomIt->second;
    }

    {
        std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);
        if(room->owner != pConn)
        {
            // 非房主无权重置筹码，仍广播当前状态以保持客户端同步
            roomLock.unlock();
            BroadcastGameState(roomId,room);
            return false;
        }

        for(std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::iterator it = room->playerStates.begin(); it != room->playerStates.end(); ++it)
        {
            it->second.chips = 2000;
            it->second.currentBet = 0;
            it->second.isFolded = false;
            it->second.isAllIn = false;
            it->second.lastAction = "Waiting";
        }

        room->pot = 0;
        room->maxBet = 0;
        room->stage = "Preflop";
        room->communityCards.clear();
        room->deck.clear();
        room->holeCards.clear();
        room->bots.clear();
        room->isPlaying = false;
        room->currentTurnUserId = 0;
    }

    BroadcastGameState(roomId,room);
    return true;
}


bool CLogicSocket::_HandleLeaveRoom(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    (void)pMsgHeader;
    (void)pPkgBody;
    (void)iBodyLength;

    uint32_t roomId = 0;
    std::vector<lpngx_connection_t> roomPlayers;
    {
        std::unique_lock<std::shared_mutex> roomMapLock(m_roomMapMutex);
        std::unordered_map<lpngx_connection_t,uint32_t>::iterator connIt = m_connRoomMap.find(pConn);
        if(connIt == m_connRoomMap.end())
        {
            return false;
        }

        roomId = connIt->second;
        std::unordered_map<uint32_t,std::shared_ptr<GameRoom>>::iterator roomIt = m_gameRooms.find(roomId);
        if(roomIt != m_gameRooms.end() && roomIt->second != NULL)
        {
            roomPlayers = roomIt->second->players;
            for(std::size_t i = 0; i < roomPlayers.size(); ++i)
            {
                m_connRoomMap.erase(roomPlayers[i]);
            }
            m_gameRooms.erase(roomIt);
        }
        else
        {
            m_connRoomMap.erase(connIt);
        }
    }

    return true;
}

void CLogicSocket::OnConnectionClosed(lpngx_connection_t pConn)
{
    if(pConn == NULL)
    {
        return;
    }

    uint32_t roomId = 0;
    std::shared_ptr<GameRoom> room;
    {
        std::unique_lock<std::shared_mutex> roomMapLock(m_roomMapMutex);
        std::unordered_map<lpngx_connection_t,uint32_t>::iterator connIt = m_connRoomMap.find(pConn);
        if(connIt == m_connRoomMap.end())
        {
            return;
        }

        roomId = connIt->second;
        m_connRoomMap.erase(connIt);

        std::unordered_map<uint32_t,std::shared_ptr<GameRoom>>::iterator roomIt = m_gameRooms.find(roomId);
        if(roomIt == m_gameRooms.end())
        {
            return;
        }

        room = roomIt->second;
    }

    std::vector<lpngx_connection_t> playersSnapshot;
    bool wasOwner = false;
    bool ownerChanged = false;
    bool destroyRoom = false;
    uint64_t newOwnerUserId = 0;

    {
        std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);

        std::vector<lpngx_connection_t>::iterator playerIt = std::find(room->players.begin(),room->players.end(),pConn);
        if(playerIt != room->players.end())
        {
            room->players.erase(playerIt);
        }

        room->playerStates.erase(pConn);
        room->playerStats.erase(pConn);
        room->holeCards.erase(pConn);

        wasOwner = (room->owner == pConn);
        if(wasOwner)
        {
            if(!room->players.empty())
            {
                room->owner = room->players[0];
                ownerChanged = true;

                std::unordered_map<lpngx_connection_t,GameRoom::PlayerStats>::const_iterator statsIt = room->playerStats.find(room->owner);
                if(statsIt != room->playerStats.end())
                {
                    newOwnerUserId = statsIt->second.userId;
                }
                else
                {
                    newOwnerUserId = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(room->owner));
                }
            }
            else
            {
                room->owner = NULL;
                destroyRoom = true;
            }
        }

        if(!destroyRoom)
        {
            playersSnapshot = room->players;
        }
    }

    if(destroyRoom)
    {
        std::unique_lock<std::shared_mutex> roomMapLock(m_roomMapMutex);
        std::unordered_map<uint32_t,std::shared_ptr<GameRoom>>::iterator roomIt = m_gameRooms.find(roomId);
        if(roomIt != m_gameRooms.end() && roomIt->second == room)
        {
            m_gameRooms.erase(roomIt);
        }
        return;
    }

    if(ownerChanged)
    {
        nlohmann::json ownerChangedJson;
        ownerChangedJson["roomId"] = roomId;
        ownerChangedJson["newOwnerUserId"] = newOwnerUserId;
        ownerChangedJson["event"] = "OwnerChanged";
        std::string ownerPayload = ownerChangedJson.dump();

        for(std::size_t i = 0; i < playersSnapshot.size(); ++i)
        {
            lpngx_connection_t playerConn = playersSnapshot[i];
            if(playerConn == NULL)
            {
                continue;
            }

            STRUC_MSG_HEADER msgHeader;
            msgHeader.pConn = playerConn;
            msgHeader.iCurrsequence = playerConn->iCurrsequence;
            SendJsonPkgToClient(&msgHeader,3002,ownerPayload);
        }
    }

    BroadcastGameState(roomId,room);
}

void CLogicSocket::BroadcastGameState(uint32_t roomId,const std::shared_ptr<GameRoom> &room)
{
    std::vector<lpngx_connection_t> playersSnapshot;
    std::string roomStateJson;

    {
        std::shared_lock<std::shared_mutex> roomLock(room->roomMutex);

        nlohmann::json state;
        state["roomId"] = roomId;
        state["pot"] = room->pot;
        state["stage"] = room->stage;
        state["currentTurnUserId"] = room->currentTurnUserId;
        state["communityCards"] = nlohmann::json::array();

        for(std::size_t i = 0; i < room->communityCards.size(); ++i)
        {
            std::string rank;
            std::string suit;
            EncodeCard(room->communityCards[i],rank,suit);

            nlohmann::json cardJson;
            cardJson["rank"] = rank;
            cardJson["suit"] = suit;
            state["communityCards"].push_back(cardJson);
        }

        state["players"] = nlohmann::json::array();
        for(std::size_t i = 0; i < room->players.size(); ++i)
        {
            lpngx_connection_t playerConn = room->players[i];
            if(playerConn == NULL)
            {
                continue;
            }

            GameRoom::PlayerState stateForPlayer;
            std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::const_iterator stIt = room->playerStates.find(playerConn);
            if(stIt != room->playerStates.end())
            {
                stateForPlayer = stIt->second;
            }

            uint64_t userId = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(playerConn));
            std::string username = "Player-" + std::to_string(reinterpret_cast<uintptr_t>(playerConn));
            std::unordered_map<lpngx_connection_t,GameRoom::PlayerStats>::iterator statsIt = room->playerStats.find(playerConn);
            if(statsIt != room->playerStats.end())
            {
                userId = statsIt->second.userId;
                username = statsIt->second.username;
                statsIt->second.chips = stateForPlayer.chips;
            }

            nlohmann::json playerJson;
            playerJson["userId"] = userId;
            playerJson["username"] = username;
            playerJson["chips"] = stateForPlayer.chips;
            playerJson["isOwner"] = (room->owner == playerConn);
            playerJson["currentBet"] = stateForPlayer.currentBet;
            playerJson["isFolded"] = stateForPlayer.isFolded;
            playerJson["isAllIn"] = stateForPlayer.isAllIn;
            playerJson["lastAction"] = stateForPlayer.lastAction;
            playerJson["isBot"] = false;
            playerJson["holeCards"] = nlohmann::json::array();

            std::unordered_map<lpngx_connection_t,std::vector<std::string>>::const_iterator cardsIt = room->holeCards.find(playerConn);
            if(cardsIt != room->holeCards.end())
            {
                for(std::size_t j = 0; j < cardsIt->second.size(); ++j)
                {
                    std::string rank;
                    std::string suit;
                    EncodeCard(cardsIt->second[j],rank,suit);

                    nlohmann::json cardJson;
                    cardJson["rank"] = rank;
                    cardJson["suit"] = suit;
                    playerJson["holeCards"].push_back(cardJson);
                }
            }

            state["players"].push_back(playerJson);
        }

        for(std::size_t i = 0; i < room->bots.size(); ++i)
        {
            const GameRoom::BotPlayer &bot = room->bots[i];

            nlohmann::json playerJson;
            playerJson["userId"] = bot.userId;
            playerJson["username"] = bot.username;
            playerJson["chips"] = bot.state.chips;
            playerJson["isOwner"] = false;
            playerJson["currentBet"] = bot.state.currentBet;
            playerJson["isFolded"] = bot.state.isFolded;
            playerJson["isAllIn"] = bot.state.isAllIn;
            playerJson["lastAction"] = bot.state.lastAction;
            playerJson["isBot"] = true;
            playerJson["holeCards"] = nlohmann::json::array();

            for(std::size_t j = 0; j < bot.holeCards.size(); ++j)
            {
                std::string rank;
                std::string suit;
                EncodeCard(bot.holeCards[j],rank,suit);

                nlohmann::json cardJson;
                cardJson["rank"] = rank;
                cardJson["suit"] = suit;
                playerJson["holeCards"].push_back(cardJson);
            }

            state["players"].push_back(playerJson);
        }

        roomStateJson = state.dump();
        playersSnapshot = room->players;
    }

    for(std::size_t i = 0; i < playersSnapshot.size(); ++i)
    {
        lpngx_connection_t playerConn = playersSnapshot[i];
        if(playerConn == NULL)
        {
            continue;
        }

        STRUC_MSG_HEADER msgHeader;
        msgHeader.pConn = playerConn;
        msgHeader.iCurrsequence = playerConn->iCurrsequence;
        SendJsonPkgToClient(&msgHeader,3001,roomStateJson);
    }
}

void CLogicSocket::FillBotsForRoom(uint32_t roomId,const std::shared_ptr<GameRoom> &room)
{
    if(room == NULL)
    {
        return;
    }

    const std::size_t humanCount = room->players.size();
    const std::size_t targetBots = humanCount >= 6 ? 0 : (6 - humanCount);
    room->bots.clear();

    for(std::size_t i = 0; i < targetBots; ++i)
    {
        GameRoom::BotPlayer bot;
        bot.userId = static_cast<uint64_t>(roomId) * 1000ULL + static_cast<uint64_t>(i + 1);
        bot.username = "Bot-" + std::to_string(i + 1);
        bot.state = GameRoom::PlayerState();
        bot.style = static_cast<int>(i % 4);
        room->bots.push_back(bot);
    }
}

void CLogicSocket::AdvanceTurn(const std::shared_ptr<GameRoom> &room,uint64_t currentUserId)
{
    if(room == NULL)
    {
        return;
    }

    std::vector<uint64_t> order;
    order.reserve(room->players.size() + room->bots.size());

    for(std::size_t i = 0; i < room->players.size(); ++i)
    {
        lpngx_connection_t conn = room->players[i];
        if(conn == NULL)
        {
            continue;
        }

        uint64_t uid = GetPlayerUserId(room,conn);
        if(uid != 0)
        {
            order.push_back(uid);
        }
    }

    for(std::size_t i = 0; i < room->bots.size(); ++i)
    {
        order.push_back(room->bots[i].userId);
    }

    if(order.empty())
    {
        room->currentTurnUserId = 0;
        return;
    }

    std::size_t startIndex = 0;
    for(std::size_t i = 0; i < order.size(); ++i)
    {
        if(order[i] == currentUserId)
        {
            startIndex = i;
            break;
        }
    }

    room->currentTurnUserId = 0;
    for(std::size_t step = 1; step <= order.size(); ++step)
    {
        std::size_t idx = (startIndex + step) % order.size();
        uint64_t uid = order[idx];

        bool active = false;
        for(std::size_t h = 0; h < room->players.size(); ++h)
        {
            lpngx_connection_t conn = room->players[h];
            if(conn == NULL || GetPlayerUserId(room,conn) != uid)
            {
                continue;
            }

            std::unordered_map<lpngx_connection_t,GameRoom::PlayerState>::const_iterator stateIt = room->playerStates.find(conn);
            if(stateIt != room->playerStates.end() && IsActivePlayer(stateIt->second))
            {
                active = true;
            }
            break;
        }

        if(!active)
        {
            for(std::size_t b = 0; b < room->bots.size(); ++b)
            {
                if(room->bots[b].userId == uid)
                {
                    active = IsActivePlayer(room->bots[b].state);
                    break;
                }
            }
        }

        if(active)
        {
            room->currentTurnUserId = uid;
            return;
        }
    }
}

void CLogicSocket::RunBotTurns(uint32_t roomId,const std::shared_ptr<GameRoom> &room)
{
    if(room == NULL)
    {
        return;
    }

    for(int round = 0; round < 12; ++round)
    {
        bool acted = false;
        {
            std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);
            uint64_t turnUserId = room->currentTurnUserId;
            if(turnUserId == 0)
            {
                return;
            }

            for(std::size_t i = 0; i < room->bots.size(); ++i)
            {
                GameRoom::BotPlayer &bot = room->bots[i];
                if(bot.userId != turnUserId)
                {
                    continue;
                }

                if(!IsActivePlayer(bot.state))
                {
                    AdvanceTurn(room,bot.userId);
                    acted = true;
                    break;
                }

                int toCall = static_cast<int>(room->maxBet) - bot.state.currentBet;
                if(toCall < 0)
                {
                    toCall = 0;
                }

                int strength = EstimateHoleStrength(bot.holeCards);
                if(bot.state.chips <= 0)
                {
                    bot.state.isAllIn = true;
                    bot.state.lastAction = "AllIn";
                }
                else if(strength <= 1 && toCall > bot.state.chips / 3)
                {
                    bot.state.isFolded = true;
                    bot.state.lastAction = "Fold";
                }
                else if(strength >= 3 && bot.state.chips > toCall + 20)
                {
                    int paid = std::min(bot.state.chips,ComputeBotRaise(bot,room->maxBet));
                    bot.state.chips -= paid;
                    bot.state.currentBet += paid;
                    room->pot += static_cast<uint32_t>(paid);
                    if(static_cast<uint32_t>(bot.state.currentBet) > room->maxBet)
                    {
                        room->maxBet = static_cast<uint32_t>(bot.state.currentBet);
                    }
                    bot.state.lastAction = "Raise";
                    if(bot.state.chips == 0)
                    {
                        bot.state.isAllIn = true;
                    }
                }
                else
                {
                    int paid = std::min(bot.state.chips,toCall);
                    bot.state.chips -= paid;
                    bot.state.currentBet += paid;
                    room->pot += static_cast<uint32_t>(paid);
                    bot.state.lastAction = (toCall == 0 ? "Check" : "Call");
                    if(bot.state.chips == 0)
                    {
                        bot.state.isAllIn = true;
                    }
                }

                AdvanceTurn(room,bot.userId);
                acted = true;
                break;
            }
        }

        if(!acted)
        {
            return;
        }

        BroadcastGameState(roomId,room);
    }
}
