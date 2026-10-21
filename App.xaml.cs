using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace ProtopteraPlayer;

public partial class App : Application
{
    private const string MutexName = "ProtopteraPlayer_SingleInstance_Mutex_v1";
    private const string PipeName  = "ProtopteraPlayer_SingleInstance_Pipe_v1";

    private Mutex? _mutex;
    private MainWindow? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? fileArg = e.Args.Length > 0 ? e.Args[0] : null;

        _mutex = new Mutex(true, MutexName, out bool isFirst);

        if (!isFirst)
        {
            // Экземпляр уже запущен: передаём путь и выходим — новый трек играет ВМЕСТО старого.
            SendToRunningInstance(fileArg);
            Shutdown();
            return;
        }

        _main = new MainWindow();
        MainWindow = _main;
        _main.Show();

        StartPipeServer();

        if (!string.IsNullOrWhiteSpace(fileArg))
            _main.OpenFile(fileArg);
    }

    private static void SendToRunningInstance(string? filePath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(filePath ?? string.Empty);
        }
        catch { /* не достучались — молча выходим */ }
    }

    private void StartPipeServer()
    {
        var t = new Thread(PipeServerLoop) { IsBackground = true, Name = "Protoptera-Pipe" };
        t.Start();
    }

    private void PipeServerLoop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);

                server.WaitForConnection();

                using var reader = new StreamReader(server);
                string? path = reader.ReadLine();

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(path))
                        _main?.OpenFile(path);
                    _main?.BringToFront();
                });
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
