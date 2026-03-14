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
//在简单场景下从JSON文本中抽取一个字符串字段
bool ExtractJsonString(const std::string &json,const char *key,std::string &value)
{
    std::string pattern = "\"";
    pattern += key;
    pattern += "\"";

    std::size_t keyPos = json.find(pattern);
    if(keyPos == std::string::npos)
    {
        return false;
    }

    std::size_t colonPos = json.find(':',keyPos + pattern.size());
    if(colonPos == std::string::npos)
    {
        return false;
    }

    std::size_t firstQuote = json.find('"',colonPos + 1);
    if(firstQuote == std::string::npos)
    {
        return false;
    }

    std::size_t secondQuote = json.find('"',firstQuote + 1);
    if(secondQuote == std::string::npos || secondQuote <= firstQuote)
    {
        return false;
    }

    value = json.substr(firstQuote + 1,secondQuote - firstQuote - 1);
    return true;
}

bool ExtractJsonUint(const std::string &json,const char *key,uint32_t &value)
{
    std::string pattern = "\"";
    pattern += key;
    pattern += "\"";

    std::size_t keyPos = json.find(pattern);
    if(keyPos == std::string::npos)
    {
        return false;
    }

    std::size_t colonPos = json.find(':',keyPos + pattern.size());
    if(colonPos == std::string::npos)
    {
        return false;
    }

    std::size_t firstDigit = json.find_first_of("0123456789",colonPos + 1);
    if(firstDigit == std::string::npos)
    {
        return false;
    }

    std::size_t endDigit = json.find_first_not_of("0123456789",firstDigit);
    std::string numStr = json.substr(firstDigit,endDigit == std::string::npos ? std::string::npos : endDigit - firstDigit);
    if(numStr.empty())
    {
        return false;
    }

    value = static_cast<uint32_t>(strtoul(numStr.c_str(),NULL,10));
    return true;
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

    std::string body(pPkgBody,iBodyLength);
    uint32_t roomId = 0;
    if(!ExtractJsonUint(body,"roomId",roomId))
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

    {
        std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);
        if(std::find(room->players.begin(),room->players.end(),pConn) == room->players.end())
        {
            room->players.push_back(pConn);
        }
    }

    SendJsonPkgToClient(pMsgHeader,1001,"{\"status\":\"ok\",\"roomId\":" + std::to_string(roomId) + "}");
    return true;
}

bool CLogicSocket::_HandleGameAction(lpngx_connection_t pConn,LPSTRUC_MSG_HEADER pMsgHeader,char *pPkgBody,unsigned short iBodyLength)
{
    if(pPkgBody == NULL || iBodyLength == 0)
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

    std::string body(pPkgBody,iBodyLength);
    std::string action;
    ExtractJsonString(body,"action",action);

    {
        std::unique_lock<std::shared_mutex> roomLock(room->roomMutex);

        if(action == "call" || action == "CALL")
        {
            uint32_t amount = 0;
            if(ExtractJsonUint(body,"amount",amount))
            {
                room->pot += amount;
            }
            else
            {
                room->pot += 10;
            }
        }
        else if(action == "fold" || action == "FOLD")
        {
            room->players.erase(std::remove(room->players.begin(),room->players.end(),pConn),room->players.end());
            room->holeCards.erase(pConn);
        }
    }

    BroadcastGameState(roomId,room);

    (void)pMsgHeader;
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
        if(room->isPlaying)
        {
            return true;
        }

        std::vector<std::string> deck = GenerateShuffledDeck();

        room->isPlaying = true;
        room->pot = 0;
        room->stage = "Preflop";
        room->communityCards.clear();
        room->holeCards.clear();

        for(std::size_t i = 0; i < room->players.size(); ++i)
        {
            lpngx_connection_t playerConn = room->players[i];
            if(playerConn == NULL || deck.size() < 2)
            {
                continue;
            }

            std::vector<std::string> cards;
            cards.push_back(deck.back());
            deck.pop_back();
            cards.push_back(deck.back());
            deck.pop_back();

            room->holeCards[playerConn] = cards;
        }

        if(!room->players.empty() && room->players[0] != NULL)
        {
            room->currentTurnUserId = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(room->players[0]));
        }
        else
        {
            room->currentTurnUserId = 0;
        }
    }

    BroadcastGameState(roomId,room);
    return true;
}

void CLogicSocket::BroadcastGameState(uint32_t roomId,const std::shared_ptr<GameRoom> &room)
{
    std::vector<lpngx_connection_t> playersSnapshot;
    std::string roomStateJson;

    {
        std::shared_lock<std::shared_mutex> roomLock(room->roomMutex);

        std::ostringstream oss;
        oss << "{\"roomId\":" << roomId
            << ",\"pot\":" << room->pot
            << ",\"playerCount\":" << room->players.size()
            << ",\"stage\":\"" << room->stage << "\""
            << ",\"currentTurnUserId\":" << room->currentTurnUserId
            << ",\"communityCards\":[";

        for(std::size_t i = 0; i < room->communityCards.size(); ++i)
        {
            if(i != 0)
            {
                oss << ",";
            }
            oss << "\"" << room->communityCards[i] << "\"";
        }
        oss << "],\"holeCards\":{";

        bool firstPlayer = true;
        for(std::unordered_map<lpngx_connection_t,std::vector<std::string>>::const_iterator it = room->holeCards.begin(); it != room->holeCards.end(); ++it)
        {
            if(!firstPlayer)
            {
                oss << ",";
            }
            firstPlayer = false;
            oss << "\"" << reinterpret_cast<uintptr_t>(it->first) << "\":[";
            for(std::size_t i = 0; i < it->second.size(); ++i)
            {
                if(i != 0)
                {
                    oss << ",";
                }
                oss << "\"" << it->second[i] << "\"";
            }
            oss << "]";
        }
        oss << "}}";

        roomStateJson = oss.str();
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
