using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace TableNote;

public sealed class EditorSignal : IDisposable
{
	public const string DefaultPipeName = "TableNote.ShowEditor";

	private readonly string _pipeName;

	private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

	private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(initialState: false);

	private int _faults;

	private bool _disposed;

	private bool _failReported;

	private Task? _loop;

	public bool IsListening { get; private set; }

	public EditorSignal(string pipeName)
	{
		_pipeName = pipeName;
	}

	public bool TryStart(Action onEdit, Action? onListeningFailed = null)
	{
		try
		{
			_loop = Task.Run(() => Listen(onEdit, onListeningFailed, _cancel.Token));
			if (!_ready.Wait(TimeSpan.FromSeconds(2L)))
			{
				IsListening = false;
				return false;
			}
			IsListening = true;
			return true;
		}
		catch (Exception)
		{
			IsListening = false;
			return false;
		}
	}

	public static bool TrySend(string? pipeName = null)
	{
		for (int i = 0; i < 8; i++)
		{
			try
			{
				using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", pipeName ?? "TableNote.ShowEditor", PipeDirection.Out);
				namedPipeClientStream.Connect(300);
				using StreamWriter streamWriter = new StreamWriter(namedPipeClientStream)
				{
					AutoFlush = true
				};
				streamWriter.WriteLine("edit");
				return true;
			}
			catch (Exception)
			{
				Thread.Sleep(40);
			}
		}
		return false;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			IsListening = false;
			_cancel.Cancel();
			_ready.Dispose();
			_cancel.Dispose();
		}
	}

	private async Task Listen(Action onEdit, Action? onListeningFailed, CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			NamedPipeServerStream server = null;
			try
			{
				server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
				if (!_disposed) _ready.Set();
				IsListening = true;
				await server.WaitForConnectionAsync(token);
				using StreamReader reader = new StreamReader(server);
				server = null;
				if (string.Equals(await reader.ReadLineAsync(token), "edit", StringComparison.OrdinalIgnoreCase))
				{
					// v2.3.1: an exception from the UI callback is not a pipe fault.
					try { onEdit(); }
					catch (Exception ex) { AppLog.Write("signal", "edit callback failed: " + ex.Message); }
				}
				_faults = 0;
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception)
			{
				_faults++;
				if (_faults >= 3)
				{
					// v2.3.1: was a permanent stop while Program.SignalReady stayed true (background mode
					// was still allowed with a dead "open again" route). Report it, then keep trying every 5 s.
					IsListening = false;
					AppLog.Write("signal", "editor pipe listener failed 3× — retrying in 5 s");
					if (!_failReported) { _failReported = true; try { onListeningFailed?.Invoke(); } catch { /* ignore */ } } // once
					_faults = 0;
					await Task.Delay(5000, token);
					continue;
				}
				await Task.Delay(200, token);
			}
			finally
			{
				server?.Dispose();
			}
		}
	}
}
