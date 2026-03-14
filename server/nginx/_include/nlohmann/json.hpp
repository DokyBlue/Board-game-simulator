#pragma once

#include <cctype>
#include <cstdint>
#include <cstdlib>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace nlohmann
{
class json
{
public:
    enum class Type
    {
        Null,
        Object,
        Array,
        String,
        Integer,
        Unsigned,
        Boolean
    };

    json() : m_type(Type::Null), m_integer(0), m_unsigned(0), m_boolean(false) {}

    static json array()
    {
        json j;
        j.m_type = Type::Array;
        return j;
    }

    static json parse(const std::string &text)
    {
        std::size_t pos = 0;
        json result = ParseValue(text,pos);
        SkipWs(text,pos);
        if(pos != text.size())
        {
            throw std::runtime_error("json parse trailing chars");
        }
        return result;
    }

    json &operator[](const std::string &key)
    {
        EnsureObject();
        return m_object[key];
    }

    const json &at(const std::string &key) const
    {
        if(m_type != Type::Object)
        {
            throw std::runtime_error("json not object");
        }
        std::map<std::string,json>::const_iterator it = m_object.find(key);
        if(it == m_object.end())
        {
            throw std::out_of_range("json key not found");
        }
        return it->second;
    }

    void push_back(const json &value)
    {
        EnsureArray();
        m_array.push_back(value);
    }

    template<typename T>
    T value(const std::string &key,const T &defaultValue) const
    {
        if(m_type != Type::Object)
        {
            return defaultValue;
        }
        std::map<std::string,json>::const_iterator it = m_object.find(key);
        if(it == m_object.end())
        {
            return defaultValue;
        }
        try
        {
            return it->second.get<T>();
        }
        catch(...)
        {
            return defaultValue;
        }
    }

    std::string dump() const
    {
        std::ostringstream oss;
        DumpTo(oss);
        return oss.str();
    }

    json &operator=(const std::string &v) { m_type = Type::String; m_string = v; return *this; }
    json &operator=(const char *v) { m_type = Type::String; m_string = (v == NULL ? "" : v); return *this; }
    json &operator=(int v) { m_type = Type::Integer; m_integer = v; return *this; }
    json &operator=(uint32_t v) { m_type = Type::Unsigned; m_unsigned = v; return *this; }
    json &operator=(uint64_t v) { m_type = Type::Unsigned; m_unsigned = v; return *this; }
    json &operator=(bool v) { m_type = Type::Boolean; m_boolean = v; return *this; }

    template<typename T>
    T get() const;

private:
    static void SkipWs(const std::string &text,std::size_t &pos)
    {
        while(pos < text.size() && std::isspace(static_cast<unsigned char>(text[pos])))
        {
            ++pos;
        }
    }

    static json ParseValue(const std::string &text,std::size_t &pos)
    {
        SkipWs(text,pos);
        if(pos >= text.size()) throw std::runtime_error("json eof");

        if(text[pos] == '{') return ParseObject(text,pos);
        if(text[pos] == '[') return ParseArray(text,pos);
        if(text[pos] == '"')
        {
            json j;
            j.m_type = Type::String;
            j.m_string = ParseStringToken(text,pos);
            return j;
        }
        if(text[pos] == '-' || std::isdigit(static_cast<unsigned char>(text[pos]))) return ParseNumber(text,pos);
        if(text.compare(pos,4,"true") == 0)
        {
            pos += 4;
            json j;
            j.m_type = Type::Boolean;
            j.m_boolean = true;
            return j;
        }
        if(text.compare(pos,5,"false") == 0)
        {
            pos += 5;
            json j;
            j.m_type = Type::Boolean;
            j.m_boolean = false;
            return j;
        }
        if(text.compare(pos,4,"null") == 0)
        {
            pos += 4;
            return json();
        }

        throw std::runtime_error("json bad token");
    }

    static json ParseObject(const std::string &text,std::size_t &pos)
    {
        json j;
        j.m_type = Type::Object;
        ++pos;
        SkipWs(text,pos);
        if(pos < text.size() && text[pos] == '}')
        {
            ++pos;
            return j;
        }

        while(pos < text.size())
        {
            SkipWs(text,pos);
            std::string key = ParseStringToken(text,pos);
            SkipWs(text,pos);
            if(pos >= text.size() || text[pos] != ':') throw std::runtime_error("json missing colon");
            ++pos;
            j.m_object[key] = ParseValue(text,pos);
            SkipWs(text,pos);
            if(pos < text.size() && text[pos] == ',')
            {
                ++pos;
                continue;
            }
            if(pos < text.size() && text[pos] == '}')
            {
                ++pos;
                return j;
            }
            throw std::runtime_error("json object syntax");
        }

        throw std::runtime_error("json object eof");
    }

    static json ParseArray(const std::string &text,std::size_t &pos)
    {
        json j;
        j.m_type = Type::Array;
        ++pos;
        SkipWs(text,pos);
        if(pos < text.size() && text[pos] == ']')
        {
            ++pos;
            return j;
        }

        while(pos < text.size())
        {
            j.m_array.push_back(ParseValue(text,pos));
            SkipWs(text,pos);
            if(pos < text.size() && text[pos] == ',')
            {
                ++pos;
                continue;
            }
            if(pos < text.size() && text[pos] == ']')
            {
                ++pos;
                return j;
            }
            throw std::runtime_error("json array syntax");
        }

        throw std::runtime_error("json array eof");
    }

    static json ParseNumber(const std::string &text,std::size_t &pos)
    {
        std::size_t start = pos;
        if(text[pos] == '-') ++pos;
        while(pos < text.size() && std::isdigit(static_cast<unsigned char>(text[pos]))) ++pos;

        std::string num = text.substr(start,pos - start);
        long long value = std::strtoll(num.c_str(),NULL,10);

        json j;
        if(value < 0)
        {
            j.m_type = Type::Integer;
            j.m_integer = static_cast<int64_t>(value);
        }
        else
        {
            j.m_type = Type::Unsigned;
            j.m_unsigned = static_cast<uint64_t>(value);
        }
        return j;
    }

    static std::string ParseStringToken(const std::string &text,std::size_t &pos)
    {
        if(pos >= text.size() || text[pos] != '"')
        {
            throw std::runtime_error("json expected string");
        }

        ++pos;
        std::string out;
        while(pos < text.size())
        {
            char c = text[pos++];
            if(c == '"')
            {
                return out;
            }
            if(c == '\\' && pos < text.size())
            {
                char esc = text[pos++];
                if(esc == '"' || esc == '\\' || esc == '/') out.push_back(esc);
                else if(esc == 'b') out.push_back('\b');
                else if(esc == 'f') out.push_back('\f');
                else if(esc == 'n') out.push_back('\n');
                else if(esc == 'r') out.push_back('\r');
                else if(esc == 't') out.push_back('\t');
                else out.push_back(esc);
            }
            else
            {
                out.push_back(c);
            }
        }
        throw std::runtime_error("json string eof");
    }

    void EnsureObject()
    {
        if(m_type != Type::Object)
        {
            m_type = Type::Object;
            m_object.clear();
        }
    }

    void EnsureArray()
    {
        if(m_type != Type::Array)
        {
            m_type = Type::Array;
            m_array.clear();
        }
    }

    static std::string Escape(const std::string &input)
    {
        std::string out;
        for(std::size_t i = 0; i < input.size(); ++i)
        {
            char c = input[i];
            if(c == '"' || c == '\\')
            {
                out.push_back('\\');
                out.push_back(c);
            }
            else if(c == '\n') out += "\\n";
            else if(c == '\r') out += "\\r";
            else if(c == '\t') out += "\\t";
            else out.push_back(c);
        }
        return out;
    }

    void DumpTo(std::ostringstream &oss) const
    {
        switch(m_type)
        {
        case Type::Null:
            oss << "null";
            break;
        case Type::Boolean:
            oss << (m_boolean ? "true" : "false");
            break;
        case Type::Integer:
            oss << m_integer;
            break;
        case Type::Unsigned:
            oss << m_unsigned;
            break;
        case Type::String:
            oss << '"' << Escape(m_string) << '"';
            break;
        case Type::Array:
            oss << "[";
            for(std::size_t i = 0; i < m_array.size(); ++i)
            {
                if(i != 0) oss << ",";
                m_array[i].DumpTo(oss);
            }
            oss << "]";
            break;
        case Type::Object:
            oss << "{";
            {
                bool first = true;
                for(std::map<std::string,json>::const_iterator it = m_object.begin(); it != m_object.end(); ++it)
                {
                    if(!first) oss << ",";
                    first = false;
                    oss << '"' << Escape(it->first) << "\":";
                    it->second.DumpTo(oss);
                }
            }
            oss << "}";
            break;
        }
    }

private:
    Type m_type;
    std::map<std::string,json> m_object;
    std::vector<json> m_array;
    std::string m_string;
    int64_t m_integer;
    uint64_t m_unsigned;
    bool m_boolean;
};

template<>
inline std::string json::get<std::string>() const
{
    if(m_type != Type::String)
    {
        throw std::runtime_error("json get<string> type mismatch");
    }
    return m_string;
}

template<>
inline int json::get<int>() const
{
    if(m_type == Type::Integer)
    {
        return static_cast<int>(m_integer);
    }
    if(m_type == Type::Unsigned)
    {
        return static_cast<int>(m_unsigned);
    }
    throw std::runtime_error("json get<int> type mismatch");
}

template<>
inline uint32_t json::get<uint32_t>() const
{
    if(m_type == Type::Unsigned)
    {
        return static_cast<uint32_t>(m_unsigned);
    }
    if(m_type == Type::Integer && m_integer >= 0)
    {
        return static_cast<uint32_t>(m_integer);
    }
    throw std::runtime_error("json get<uint32_t> type mismatch");
}

} // namespace nlohmann
