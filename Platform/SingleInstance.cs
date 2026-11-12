using System;
using System.IO;

namespace TableNote;

public sealed class SingleInstance : IDisposable
{
	private readonly FileStream _stream;

	private bool _released;

	private SingleInstance(FileStream stream)
	{
		_stream = stream;
	}

	public static string DefaultLockPath()
	{
		string text = Path.GetDirectoryName(NoteStore.DefaultPath());
		if (string.IsNullOrEmpty(text))
		{
			text = Path.GetTempPath();
		}
		return Path.Combine(text, "instance.lock");
	}

	public static SingleInstance? TryAcquire(string lockPath)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(lockPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			FileStream fileStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			if (!OperatingSystem.IsMacOS())
			{
				try
				{
					fileStream.Lock(0L, 1L);
				}
				catch (IOException)
				{
					fileStream.Dispose();
					return null;
				}
			}
			return new SingleInstance(fileStream);
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	public void Dispose()
	{
		if (_released)
		{
			return;
		}
		_released = true;
		if (!OperatingSystem.IsMacOS())
		{
			try
			{
				_stream.Unlock(0L, 1L);
			}
			catch (IOException)
			{
			}
		}
		_stream.Dispose();
	}
}
