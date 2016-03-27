using DnsRip.Interfaces;
using DnsRip.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DnsRip
{
    public partial class DnsRip
    {
        public class Resolver
        {
            public Resolver(IDnsRipRequest request, int retries = 3, int secondsTimeout = 1)
            {
                _request = request;
                _retries = retries;
                _secondsTimeout = secondsTimeout;
            }

            private readonly IDnsRipRequest _request;
            private readonly int _retries;
            private readonly int _secondsTimeout;

            public IEnumerable<DnsRipResponse> Resolve()
            {
                var question = new Question(_request.Query, _request.Type);
                var request = new Request(question);

                request.Header.Id = (ushort)(new Random()).Next();
                request.Header.Rd = _request.IsRecursive;

                var response = new List<DnsRipResponse>();
                var responseMessage = new byte[512];
                var attempts = 0;

                while (attempts <= _retries)
                {
                    attempts++;

                    foreach (var server in _request.Servers)
                    {
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout,
                            _secondsTimeout * 1000);

                        try
                        {
                            socket.SendTo(request.Data, new IPEndPoint(IPAddress.Parse(server), 53));

                            var intReceived = socket.Receive(responseMessage);
                            var data = new byte[intReceived];

                            Array.Copy(responseMessage, data, intReceived);

                            var response1 = new Response1(new IPEndPoint(IPAddress.Parse(server), 53), data);

                            foreach (var resp in response1.Answers)
                            {
                                response.Add(new DnsRipResponse
                                {
                                    Host = resp.Name,
                                    Type = resp.Type,
                                    Record = resp.Record.ToString(),
                                    Ttl = resp.Ttl
                                });
                            }

                            return response;
                        }
                        catch (SocketException)
                        {
                            //Verbose(string.Format(";; Connection to nameserver {0} failed", (intDnsServer + 1)));
                            continue; // next try
                        }
                        finally
                        {
                            //m_Unique++;

                            // close the socket
                            socket.Close();
                        }
                    }

                    //var responseTimeout = new Response1();
                    //responseTimeout.Error = "Timeout Error";
                    //return responseTimeout;
                    return null;
                }

                return null;
            }
        }
    }

    public class Response1
    {
        public Response1(IPEndPoint ipEndPoint, byte[] data)
        {
            Error = "";
            Server = ipEndPoint;
            TimeStamp = DateTime.Now;
            MessageSize = data.Length;

            var rr = new RecordReader(data);

            Questions = new List<Question>();
            Answers = new List<AnswerRr>();
            Authorities = new List<AuthorityRr>();
            Additionals = new List<AdditionalRr>();

            Header = new Header1(rr);

            for (var intI = 0; intI < Header.QdCount; intI++)
            {
                Questions.Add(new Question(rr));
            }

            for (var intI = 0; intI < Header.AnCount; intI++)
            {
                Answers.Add(new AnswerRr(rr));
            }

            for (var intI = 0; intI < Header.NsCount; intI++)
            {
                Authorities.Add(new AuthorityRr(rr));
            }

            for (var intI = 0; intI < Header.ArCount; intI++)
            {
                Additionals.Add(new AdditionalRr(rr));
            }
        }

        public string Error;
        public IPEndPoint Server;
        public DateTime TimeStamp;
        public int MessageSize;
        public List<Question> Questions;
        public List<AnswerRr> Answers;
        public List<AuthorityRr> Authorities;
        public List<AdditionalRr> Additionals;
        public Header1 Header;
    }

    public class AnswerRr : Rr
    {
        public AnswerRr(RecordReader br)
            : base(br)
        {
        }
    }

    public class AuthorityRr : Rr
    {
        public AuthorityRr(RecordReader br)
            : base(br)
        {
        }
    }

    public class AdditionalRr : Rr
    {
        public AdditionalRr(RecordReader br)
            : base(br)
        {
        }
    }

    public class Rr
    {
        public Rr(RecordReader rr)
        {
            TimeLived = 0;
            Name = rr.ReadDomainName();
            Type = (DnsRip.QueryType)rr.ReadUInt16();
            Class = (DnsRip.QueryClass)rr.ReadUInt16();
            Ttl = rr.ReadUInt32();
            RdLength = rr.ReadUInt16();
            Record = rr.ReadRecord(Type, RdLength);
            Record.Rr = this;
        }

        public int TimeLived;
        public string Name;
        public DnsRip.QueryType Type;
        public DnsRip.QueryClass Class;
        public ushort RdLength;
        public Record Record;

        private uint _ttl;

        public uint Ttl
        {
            get { return (uint)Math.Max(0, _ttl - TimeLived); }
            set { _ttl = value; }
        }
    }

    public abstract class Record
    {
        public Rr Rr;
    }

    public class RecordReader
    {
        public RecordReader(byte[] data)
        {
            _data = data;
            _position = 0;
        }

        public RecordReader(byte[] data, int position)
        {
            _data = data;
            _position = position;
        }

        private readonly byte[] _data;
        private int _position;

        public string ReadDomainName()
        {
            var name = new StringBuilder();
            int length;

            while ((length = ReadByte()) != 0)
            {
                if ((length & 0xc0) == 0xc0)
                {
                    var newRecordReader = new RecordReader(_data, (length & 0x3f) << 8 | ReadByte());

                    name.Append(newRecordReader.ReadDomainName());

                    return name.ToString();
                }

                while (length > 0)
                {
                    name.Append(ReadChar());
                    length--;
                }

                name.Append('.');
            }

            return name.Length == 0 ? "." : name.ToString();
        }

        public byte ReadByte()
        {
            return _position >= _data.Length ? (byte)0 : _data[_position++];
        }

        public byte[] ReadBytes(int intLength)
        {
            var list = new byte[intLength];

            for (var intI = 0; intI < intLength; intI++)
                list[intI] = ReadByte();

            return list;
        }

        public char ReadChar()
        {
            return (char)ReadByte();
        }

        public ushort ReadUInt16()
        {
            return (ushort)(ReadByte() << 8 | ReadByte());
        }

        public ushort ReadUInt16(int offset)
        {
            _position += offset;

            return ReadUInt16();
        }

        public uint ReadUInt32()
        {
            return (uint)(ReadUInt16() << 16 | ReadUInt16());
        }

        public Record ReadRecord(DnsRip.QueryType type, int length)
        {
            switch (type)
            {
                case DnsRip.QueryType.A:
                    return new RecordA(this);

                case DnsRip.QueryType.CNAME:
                    return new RecordCName(this);

                default:
                    return new RecordUnknown(this);
            }
        }
    }

    public class Request
    {
        public Request(Question question)
        {
            Header = new Header1
            {
                OpCode = DnsRip.OpCode.Query,
                QdCount = 1
            };

            _question = question;
        }

        public Header1 Header;

        private readonly Question _question;

        public byte[] Data
        {
            get
            {
                var data = new List<byte>();
                data.AddRange(Header.Data);
                data.AddRange(_question.Data);
                return data.ToArray();
            }
        }
    }

    public class Header1
    {
        public Header1()
        {
        }

        public Header1(RecordReader rr)
        {
            Id = rr.ReadUInt16();
            Flags = rr.ReadUInt16();
            QdCount = rr.ReadUInt16();
            AnCount = rr.ReadUInt16();
            NsCount = rr.ReadUInt16();
            ArCount = rr.ReadUInt16();
        }

        public ushort Id;
        public ushort Flags;
        public ushort QdCount;
        public ushort AnCount;
        public ushort NsCount;
        public ushort ArCount;

        public DnsRip.OpCode OpCode
        {
            get { return (DnsRip.OpCode)GetBits(Flags, 11, 4); }
            set { Flags = SetBits(Flags, 11, 4, (ushort)value); }
        }

        public bool Rd
        {
            get
            {
                return GetBits(Flags, 8, 1) == 1;
            }
            set
            {
                Flags = SetBits(Flags, 8, 1, value);
            }
        }

        public byte[] Data
        {
            get
            {
                var data = new List<byte>();
                data.AddRange(DnsRip.Utilities.ToNetByteOrder(Id));
                data.AddRange(DnsRip.Utilities.ToNetByteOrder(Flags));
                data.AddRange(DnsRip.Utilities.ToNetByteOrder(QdCount));
                data.AddRange(DnsRip.Utilities.ToNetByteOrder(AnCount));
                data.AddRange(DnsRip.Utilities.ToNetByteOrder(NsCount));
                data.AddRange(DnsRip.Utilities.ToNetByteOrder(ArCount));
                return data.ToArray();
            }
        }

        private static ushort GetBits(ushort oldValue, int position, int length)
        {
            if (length <= 0 || position >= 16)
                return 0;

            var mask = (2 << (length - 1)) - 1;

            return (ushort)((oldValue >> position) & mask);
        }

        private static ushort SetBits(ushort oldValue, int position, int length, ushort newValue)
        {
            if (length <= 0 || position >= 16)
                return oldValue;

            var mask = (2 << (length - 1)) - 1;

            oldValue &= (ushort)~(mask << position);
            oldValue |= (ushort)((newValue & mask) << position);

            return oldValue;
        }

        private static ushort SetBits(ushort oldValue, int position, int length, bool blnValue)
        {
            return SetBits(oldValue, position, length, blnValue ? (ushort)1 : (ushort)0);
        }
    }

    public class Question
    {
        public Question(string query, DnsRip.QueryType type)
        {
            Query = query;
            Type = type;
            Class = DnsRip.QueryClass.IN;
        }

        public Question(RecordReader rr)
        {
            Query = rr.ReadDomainName();
            Type = (DnsRip.QueryType)rr.ReadUInt16();
            Class = (DnsRip.QueryClass)rr.ReadUInt16();
        }

        private string _query;

        public DnsRip.QueryType Type;
        public DnsRip.QueryClass Class;

        public string Query
        {
            get { return _query; }
            set
            {
                _query = DnsRip.Utilities.ToNameFormat(value);
            }
        }

        public byte[] Data
        {
            get
            {
                var data = new List<byte>();
                data.AddRange(WriteName(Query));
                data.AddRange(DnsRip.Utilities.ToNetByteOrder((ushort)Type));
                data.AddRange(DnsRip.Utilities.ToNetByteOrder((ushort)Class));
                return data.ToArray();
            }
        }

        private static IEnumerable<byte> WriteName(string src)
        {
            src = DnsRip.Utilities.ToNameFormat(src);

            if (src == ".")
                return new byte[1];

            var sb = new StringBuilder();
            int intI, intJ, intLen = src.Length;

            sb.Append('\0');

            for (intI = 0, intJ = 0; intI < intLen; intI++, intJ++)
            {
                sb.Append(src[intI]);

                if (src[intI] != '.')
                    continue;

                sb[intI - intJ] = (char)(intJ & 0xff);
                intJ = -1;
            }

            sb[sb.Length - 1] = '\0';

            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        public override string ToString()
        {
            return $"{Query,-32}\t{Class}\t{Type}";
        }
    }

    public class RecordA : Record
    {
        public RecordA(RecordReader recordReader)
        {
            Address = new IPAddress(recordReader.ReadBytes(4));
        }

        public IPAddress Address;

        public override string ToString()
        {
            return Address.ToString();
        }
    }

    public class RecordCName : Record
    {
        public string CName;

        public RecordCName(RecordReader rr)
        {
            CName = rr.ReadDomainName();
        }

        public override string ToString()
        {
            return CName;
        }
    }

    public class RecordUnknown : Record
    {
        public RecordUnknown(RecordReader rr)
        {
            var rdLength = rr.ReadUInt16(-2);
            RData = rr.ReadBytes(rdLength);
        }

        public byte[] RData;
    }
}