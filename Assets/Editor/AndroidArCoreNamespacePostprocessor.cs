using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor.Android;

public class AndroidArCoreNamespacePostprocessor : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string aarPath = Path.Combine(path, "libs", "unityandroidpermissions.aar");
        if (!File.Exists(aarPath))
        {
            return;
        }

        string tempPath = aarPath + ".tmp";
        bool changed = false;

        using (ZipArchive input = ZipFile.OpenRead(aarPath))
        using (ZipArchive output = ZipFile.Open(tempPath, ZipArchiveMode.Create))
        {
            foreach (ZipArchiveEntry inputEntry in input.Entries)
            {
                ZipArchiveEntry outputEntry = output.CreateEntry(inputEntry.FullName, CompressionLevel.Optimal);
                using Stream inputStream = inputEntry.Open();
                using Stream outputStream = outputEntry.Open();

                if (inputEntry.FullName == "AndroidManifest.xml")
                {
                    using var reader = new StreamReader(inputStream, Encoding.UTF8);
                    string manifest = reader.ReadToEnd();
                    string patchedManifest = manifest.Replace(
                        "package=\"com.google.ar.core\"",
                        "package=\"com.unity.androidpermissions\""
                    );

                    changed = changed || patchedManifest != manifest;
                    byte[] bytes = Encoding.UTF8.GetBytes(patchedManifest);
                    outputStream.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    inputStream.CopyTo(outputStream);
                }
            }
        }

        if (changed)
        {
            File.Delete(aarPath);
            File.Move(tempPath, aarPath);
        }
        else
        {
            File.Delete(tempPath);
        }
    }
}
