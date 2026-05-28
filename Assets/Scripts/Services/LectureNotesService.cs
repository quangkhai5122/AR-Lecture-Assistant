using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class LectureNotesService
{
    private readonly string notesPath;

    public LectureNotesService(string fileName)
    {
        string safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? "lecture_notes.md"
            : fileName.Trim();

        notesPath = Path.Combine(Application.persistentDataPath, safeFileName);
    }

    public string NotesPath => notesPath;

    public string ReadAll()
    {
        return File.Exists(notesPath) ? File.ReadAllText(notesPath, Encoding.UTF8) : string.Empty;
    }

    public void AppendTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;

        string directory = Path.GetDirectoryName(notesPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("## " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine(transcript.Trim());
        builder.AppendLine();

        File.AppendAllText(notesPath, builder.ToString(), Encoding.UTF8);
    }

    public string ExportCopy()
    {
        string directory = Path.GetDirectoryName(notesPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string exportPath = Path.Combine(
            string.IsNullOrEmpty(directory) ? Application.persistentDataPath : directory,
            "lecture_notes_export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md"
        );
        File.WriteAllText(exportPath, ReadAll(), Encoding.UTF8);
        return exportPath;
    }

    public void Delete()
    {
        if (File.Exists(notesPath))
        {
            File.Delete(notesPath);
        }
    }
}
