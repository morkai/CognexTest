using Cognex.InSight.NativeMode;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml;

namespace CognexTest
{
  class Program
  {
    static int ONLINE_TIME = 5000;
    static int ONLINE_SLEEP = 200;
    static int PROGRAM_TIME = 5000;
    static int PROGRAM_SLEEP = 200;

    static int repeat = 0;
    static string host = "10.0.0.151";
    static string user = "admin";
    static string pass = "";
    static string program = "WALKNER-IPT-VISION";

    static bool cancelled = false;
    static CvsNativeModeClient client;

    static void ParseArgs(string[] args)
    {
      for (var i = 0; i < args.Length; ++i)
      {
        var k = args[i];

        switch (k)
        {
          case "--repeat":
            repeat = int.Parse(args[++i]);

            if (repeat < 0)
            {
              throw new Exception("--repeat must be greater than or equal to 0.");
            }
            break;

          case "--host":
            host = args[++i];
            break;

          case "--user":
            user = args[++i];
            break;

          case "--pass":
            pass = args[++i];
            break;

          case "--program":
            program = PrepareProgramName(args[++i]);
            break;
        }
      }
    }

    static void Main(string[] args)
    {
      try
      {
        ParseArgs(args);
      }
      catch (Exception x)
      {
        Console.Error.WriteLine("Failed to parse arguments: {0}", x.Message);
        Console.Error.WriteLine("ERR_INVALID_ARGS");
        Environment.Exit(1);
      }

      Console.CancelKeyPress += Console_CancelKeyPress;

      client = new CvsNativeModeClient();

      Console.Error.WriteLine($"Connecting to {host} as {user}...");

      try
      {
        client.Connect(host, user, pass);
      }
      catch (Exception x)
      {
        Console.Error.WriteLine("Failed to connect: {0}", x.Message);
        Console.Error.WriteLine("ERR_CONNECTION_FAILURE");
        Environment.Exit(1);
      }

      while (!cancelled)
      {
        try
        {
          Run();
        }
        catch (Exception x)
        {
          if (cancelled)
          {
            Console.Error.WriteLine("ERR_CANCELLED");
          }
          else if (x.Message.StartsWith("ERR_"))
          {
            Console.Error.WriteLine(x.Message);
          }
          else
          {
            Console.Error.WriteLine(x.Message);
            Console.Error.WriteLine("ERR_EXCEPTION");
          }

          if (repeat == 0)
          {
            Environment.Exit(1);
          }
        }

        if (repeat == 0)
        {
          break;
        }

        if (!cancelled)
        {
          Thread.Sleep(repeat);
        }

        Console.Error.WriteLine();
      }

      try
      {
        if (client.Connected)
        {
          Console.Error.WriteLine("Closing the connection...");

          client.Disconnect();
        }

        client = null;
      }
      catch (Exception) { }
    }

    private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
      e.Cancel = true;
      cancelled = true;
    }

    private static XmlElement SendCommand(string command)
    {
      try
      {
        client.SendCommand(command);
      }
      catch (Exception x)
      {
        Console.Error.WriteLine($"Command failed: {command}: {x.Message}");

        throw new Exception("ERR_COMMAND_FAILED");
      }

      var doc = new XmlDocument();

      doc.LoadXml(@"<?xml version=""1.0""?>" + client.LastResponseString);

      return doc.DocumentElement;
    }

    private static void Run()
    {
      CheckProgram();
      SelectProgram();
      Trigger();
    }

    private static void CheckCancel()
    {
      if (cancelled)
      {
        throw new Exception();
      }
    }

    private static void CheckProgram()
    {
      Console.Error.WriteLine("Checking the program...");

      var nodes = SendCommand("GET FILELIST").SelectNodes("//FileName");

      Console.Error.WriteLine($"Found {nodes.Count} programs.");

      var availablePrograms = new SortedSet<string>();

      foreach (XmlNode node in nodes)
      {
        availablePrograms.Add(PrepareProgramName(node.InnerText));
      }

      if (!availablePrograms.Contains(program))
      {
        Console.Error.WriteLine($"Program not found: {program}");

        throw new Exception("ERR_PROGRAM_NOT_FOUND");
      }
    }

    private static string PrepareProgramName(string program)
    {
      return program.Trim().ToUpperInvariant().Replace(".JOB", "");
    }

    private static bool CheckOnline()
    {
      CheckCancel();

      Console.Error.WriteLine("Checking online status...");

      if (SendCommand("GO").SelectSingleNode("//Online").InnerText.Equals("1"))
      {
        Console.Error.WriteLine("Device is online.");

        return true;
      }

      Console.Error.WriteLine("Device is offline.");

      return false;
    }

    private static void ToggleOnline(bool online)
    {
      CheckCancel();

      Console.Error.WriteLine(online ? "Going online..." : "Going offline...");

      SendCommand(online ? "SO1" : "SO0");

      var startedAt = DateTime.Now;

      while (!cancelled && DateTime.Now.Subtract(startedAt).TotalMilliseconds <= ONLINE_TIME)
      {
        CheckCancel();

        if (CheckOnline() != online)
        {
          Console.Error.WriteLine("...not yet {0}...", online ? "online" : "offline");

          Thread.Sleep(ONLINE_SLEEP);

          continue;
        }

        Console.Error.WriteLine("...went {0}.", online ? "online" : "offline");

        return;
      }

      throw new Exception("ERR_TOGGLE_ONLINE_FAILED");
    }

    private static void SelectProgram()
    {
      CheckCancel();

      Console.Error.WriteLine($"Selecting program {program}...");

      var oldSelectedProgram = PrepareProgramName(SendCommand("GF").SelectSingleNode("//FileName").InnerText);

      if (oldSelectedProgram == program)
      {
        Console.Error.WriteLine("Program already selected.");

        return;
      }

      if (CheckOnline())
      {
        ToggleOnline(false);
      }

      CheckCancel();

      SendCommand($"LF{program}.JOB");

      var startedAt = DateTime.Now;

      while (!cancelled && DateTime.Now.Subtract(startedAt).TotalMilliseconds <= PROGRAM_TIME)
      {
        CheckCancel();

        var newSelectedProgram = PrepareProgramName(SendCommand("GF").SelectSingleNode("//FileName").InnerText);

        if (newSelectedProgram != program)
        {
          Console.Error.WriteLine("...program not selected yet...");

          Thread.Sleep(PROGRAM_SLEEP);

          continue;
        }

        Console.Error.WriteLine("...program selected.");

        return;
      }

      throw new Exception("ERR_PROGRAM_SELECTION_FAILED");
    }

    private static void Trigger()
    {
      CheckCancel();

      Console.Error.WriteLine($"Triggering...");

      if (!CheckOnline())
      {
        ToggleOnline(true);
      }

      CheckCancel();

      var res = SendCommand($"SW8").InnerText.Trim();

      if (res != "1")
      {
        throw new Exception("ERR_TRIGGER_FAILED");
      }

      Console.Error.WriteLine("Triggered.");
    }
  }
}
