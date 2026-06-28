using System;
using System.IO;

namespace ETL_SQL.Engine.Handlers;

internal sealed class DatasetFileTransaction : IDisposable
{
    private readonly string _destinationPath;
    private readonly string _backupPath;
    private bool _committed;
    private bool _completed;

    private DatasetFileTransaction(string destinationPath)
    {
        _destinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(_destinationPath)
            ?? throw new InvalidOperationException("Dataset destination has no parent directory.");
        Directory.CreateDirectory(directory);

        var token = Guid.NewGuid().ToString("N");
        var baseName = Path.GetFileNameWithoutExtension(_destinationPath);
        var extension = Path.GetExtension(_destinationPath);
        StagingPath = Path.Combine(directory, $".{baseName}.tmp-{token}{extension}");
        _backupPath = Path.Combine(directory, $".{baseName}.bak-{token}{extension}");
    }

    public string StagingPath { get; }

    public static DatasetFileTransaction Create(string destinationPath) =>
        new(destinationPath);

    public void Commit()
    {
        var staged = new FileInfo(StagingPath);
        if (!staged.Exists || staged.Length == 0)
            throw new InvalidDataException("Dataset output was not created or is empty.");

        if (File.Exists(_destinationPath))
            File.Copy(_destinationPath, _backupPath, overwrite: true);

        try
        {
            File.Move(StagingPath, _destinationPath, overwrite: true);
            _committed = true;
        }
        catch
        {
            RestoreBackup();
            throw;
        }
    }

    public void Complete()
    {
        _completed = true;
        SafeDelete(_backupPath);
    }

    public void Dispose()
    {
        if (_committed && !_completed)
        {
            if (File.Exists(_backupPath))
                File.Move(_backupPath, _destinationPath, overwrite: true);
            else
                SafeDelete(_destinationPath);
        }

        SafeDelete(StagingPath);
        SafeDelete(_backupPath);
    }

    public static void Cleanup(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            SafeDelete(path);
    }

    private void RestoreBackup()
    {
        if (File.Exists(_backupPath))
            File.Move(_backupPath, _destinationPath, overwrite: true);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort; startup reconciliation removes abandoned dataset staging files.
        }
    }
}
