using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace PingEx
{
  class Program
  {
    class PingState
    {      
      Object __lock = new Object();
      Byte[] m_data = null;
      public Int32 Length
      {
        get
        {
          return m_data.Length;
        }
        set
        {
          m_data = new Byte[value];
          Thread.Sleep(1);
          var r = new Random();
          r.NextBytes(m_data);
        }
      }
      public Byte[] Data { get { return m_data; } }
      public String Host { get; set; }
      public IPAddress Address { get; set; }
      public IPAddress RAddress { get; set; }
      public Int32 Timeout { get; set; } = 1200;
      public PingOptions options { get; set; } = new PingOptions();
      public String Status = "Wait";
      Ping pingSender = new Ping();
      Boolean Wait = false;
      public Int64 Count { get; protected set; }
      public Int64 ErrorCount { get; protected set; }
      Int64 SumTimeout = 0;
      public Int64 TimeoutAvg { get { return Count > 0 ? SumTimeout / Count : 0; } }
      public Int64 TimeoutOld { get; protected set; }
      public void Send()
      {
        lock (__lock)
        {
          if (!Wait)
          {            
            pingSender.SendAsync(Address, Timeout, Data, options, this);
            Wait = true;
          }
        }
      }
      public PingState(String host, AddressFamily fam = AddressFamily.Unknown)
      {
        Host = host;
        Length = 100;
        pingSender.PingCompleted += new PingCompletedEventHandler(PingCompletedCallback);
        IPAddress A;
        if (!IPAddress.TryParse(host, out A))
        {
          var list = from a in Dns.GetHostEntry(Host).AddressList select a;
          if (fam != AddressFamily.Unknown)
            list = from a in list where a.AddressFamily == fam select a;
          if (list.Count() == 0)
            throw new Exception("Host not resolve " + Host);
          A = list.First();
        }
        Address = A;
      }
      private static void PingCompletedCallback(object sender, PingCompletedEventArgs e)
      {
        PingState ps = (PingState)e.UserState;
        ps.RAddress = e.Reply.Address;
        if (e.Reply.Status == IPStatus.TimedOut)
        {
          ps.ErrorCount += 1;
          ps.Status = "TimedOut";
        }
        else if (e.Reply.Status == IPStatus.Success)
        {
          ps.Count += 1;
          ps.SumTimeout += e.Reply.RoundtripTime;
          ps.TimeoutOld = e.Reply.RoundtripTime;
          ps.Status = "OK";          
        }
        else if (e.Reply.Status == IPStatus.TtlExpired)
        {
          ps.Count += 1;
          ps.SumTimeout += e.Reply.RoundtripTime;
          ps.TimeoutOld = e.Reply.RoundtripTime;
          ps.Status = "TTL Expired";

        }
        else
        {
          ps.ErrorCount += 1;
          ps.Status = Enum.GetName(typeof(IPStatus), e.Reply.Status);
        }
        lock (ps.__lock)
        {
          ps.Wait = false;
        }
      }
    }

    static void Help()
    {
      Console.Write("pingex [-i <TTL>] [-l <Size>] node1 [node2 [-i <TTL>] [-l <Size>]] [...]");
    }
    static Boolean CmdAddress(IPAddress A, IPAddress B)
    {
      if (A == null)
        return false;
      if (B == null)
        return false;
      if (A.AddressFamily != B.AddressFamily)
        return false;
      var a = A.GetAddressBytes();
      var b = B.GetAddressBytes();
      if (a.Length != b.Length)
        return false;
      for (Int32 i = 0; i < a.Length; i++)
        if (a[i] != b[i])
          return false;
      return true;
    }
    static void Main(string[] args)
    {
      Int32 Len = 100;
      PingOptions options = new PingOptions();     
      Boolean ErrorArg = false;
      PingState Old = null;
      var Address = new List<PingState>();
      for (Int32 i = 0; i < args.Length  && !ErrorArg;)
      {
        if (args[i] == "-i" && i + 1 < args.Length)
        {
          Int32 TTL = 0;
          if (Int32.TryParse(args[i], out TTL))
          {
            if (Old == null)
              options.Ttl = TTL;
            else
              Old.options.Ttl = TTL; 
          }
          else
            ErrorArg = true;
          i += 2;
        }
        else if (args[i] == "-l" && i + 1 < args.Length)
        {
          Int32 LEN = 0;
          if (Int32.TryParse(args[i], out LEN))
          {
            if (Old == null)
              Len = LEN;
            else
              Old.Length = LEN;
          }
          else
            ErrorArg = true;
          i += 2;
        }
        else
        {
          Old = new PingState(args[i], AddressFamily.InterNetwork);         
          Old.options.Ttl = options.Ttl;
          Old.Length = Len;
          Address.Add(Old);
          i += 1;
        }
      }
      if (ErrorArg)
      {
        Help();
        return;
      }
                  
      while (true)
      {
        foreach (var a in Address)
          a.Send();
        Console.Clear();
        Console.SetCursorPosition(0, 0);
        foreach (var a in Address)
        {
          Console.ForegroundColor = ConsoleColor.White;
          Console.Write(a.Host);
          Console.Write(" ");
          if (a.Count > 0)
          {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write(a.TimeoutOld);
            Console.Write("("+a.TimeoutAvg+")");            
          }
          else
          {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Write("-");            
          }
          if (a.Status=="OK")
            Console.ForegroundColor = ConsoleColor.DarkGreen;
          else
            Console.ForegroundColor = ConsoleColor.DarkRed;
          Console.Write(" " + a.Status);
          if (a.ErrorCount > 0)
          {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Write(" (" + a.ErrorCount + ")");
          }
          if (a.RAddress != null && !CmdAddress(a.RAddress, a.Address))
          {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.Write(" (" + a.RAddress + ")");
          }

          Console.WriteLine();
        }
        Thread.Sleep(500);
      }
      // Create a buffer of 32 bytes of data to be transmitted.
      /*int timeout = 1200;
      PingReply reply = pingSender.Send(args[0], timeout, buffer, options);

      if (reply.Status == IPStatus.Success)
      {
        Console.WriteLine("Address: {0}", reply.Address.ToString());
        Console.WriteLine("RoundTrip time: {0}", reply.RoundtripTime);
        Console.WriteLine("Time to live: {0}", reply.Options.Ttl);
        Console.WriteLine("Don't fragment: {0}", reply.Options.DontFragment);
        Console.WriteLine("Buffer size: {0}", reply.Buffer.Length);
      }*/
    }
  }
}
