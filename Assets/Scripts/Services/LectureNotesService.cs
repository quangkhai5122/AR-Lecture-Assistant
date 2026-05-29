using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class LectureNotesService
{
    private const string ExportFolderName = "ARLectureAssistant";

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
        AppendSection(string.Empty, transcript);
    }

    public void AppendSection(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        string directory = Path.GetDirectoryName(notesPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("## " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine("### " + title.Trim());
        }
        builder.AppendLine(content.Trim());
        builder.AppendLine();

        File.AppendAllText(notesPath, builder.ToString(), Encoding.UTF8);
    }

    public string ExportCopy()
    {
        string fileName = "lecture_notes_export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md";
        string content = ReadAll();

#if UNITY_ANDROID && !UNITY_EDITOR
        string androidExportPath = TryExportToAndroidDownloads(fileName, content);
        if (!string.IsNullOrWhiteSpace(androidExportPath))
        {
            return androidExportPath;
        }
#endif

        string directory = Path.GetDirectoryName(notesPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string exportPath = Path.Combine(
            string.IsNullOrEmpty(directory) ? Application.persistentDataPath : directory,
            fileName
        );
        File.WriteAllText(exportPath, content, Encoding.UTF8);
        return exportPath;
    }

    public void Delete()
    {
        if (File.Exists(notesPath))
        {
            File.Delete(notesPath);
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static string TryExportToAndroidDownloads(string fileName, string content)
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var values = new AndroidJavaObject("android.content.ContentValues"))
            using (var downloads = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            {
                values.Call("put", "_display_name", fileName);
                values.Call("put", "mime_type", "text/markdown");
                values.Call("put", "relative_path", "Download/" + ExportFolderName);

                using (var collection = downloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
                using (var uri = resolver.Call<AndroidJavaObject>("insert", collection, values))
                {
                    if (uri == null)
                    {
                        return string.Empty;
                    }

                    using (var outputStream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        if (outputStream == null)
                        {
                            return string.Empty;
                        }

                        byte[] bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
                        outputStream.Call("write", bytes);
                        outputStream.Call("flush");
                        outputStream.Call("close");
                    }
                }
            }

            return "Downloads/" + ExportFolderName + "/" + fileName;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LectureNotesService] Android public export failed: " + ex.Message);
            return string.Empty;
        }
    }
#endif
}
