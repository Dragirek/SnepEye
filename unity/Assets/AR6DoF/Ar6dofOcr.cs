using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AR6DoF
{
    /// <summary>
    /// Lecture de texte sur une image avec la reconnaissance de texte (OCR) integree a Windows 10 / 11 (Windows.Media.Ocr, hors ligne, rien n'est envoye).
    /// Rien a installer : un script PowerShell lance seulement quand on le demande, dans un autre fil que Unity ; il s'arrete tout seul. L'image est agrandie
    /// x2 si elle est petite (l'OCR lit mieux un texte d'au moins ~20 pixels de haut). Il faut qu'une langue avec OCR soit installee dans Windows
    /// (Parametres &gt; Heure et langue &gt; Langue) : sinon le message d'erreur le dit.
    /// </summary>
    public static class Ar6dofOcr
    {
        public class Job
        {
            public volatile bool done;
            public string error = "";
            public List<string> lines = new List<string>();
        }

        /// <summary>Lance la lecture de 'imagePath' dans un autre fil et rend tout de suite un travail dont 'done' passe a vrai a la fin. Le fichier image est supprime ensuite.</summary>
        public static Job Start(string imagePath)
        {
            var job = new Job();
            Task.Run(() =>
            {
                try { Run(imagePath, job); }
                catch (Exception e) { job.error = e.Message; }
                finally
                {
                    try { File.Delete(imagePath); } catch (Exception) { }
                    job.done = true;
                }
            });
            return job;
        }

        static string Script(string path)
        {
            string p = path.Replace("'", "''");
            return
                "$ErrorActionPreference = 'Stop'\n" +
                "try {\n" +
                "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)\n" +
                "Add-Type -AssemblyName System.Runtime.WindowsRuntime\n" +
                "$null = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Storage.FileAccessMode,Windows.Storage,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Graphics.Imaging.BitmapTransform,Windows.Graphics.Imaging,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Graphics.Imaging.BitmapPixelFormat,Windows.Graphics.Imaging,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Graphics.Imaging.BitmapAlphaMode,Windows.Graphics.Imaging,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Graphics.Imaging.ExifOrientationMode,Windows.Graphics.Imaging,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Graphics.Imaging.ColorManagementMode,Windows.Graphics.Imaging,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Media.Ocr.OcrEngine,Windows.Media.Ocr,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Media.Ocr.OcrResult,Windows.Media.Ocr,ContentType=WindowsRuntime]\n" +
                "$null = [Windows.Globalization.Language,Windows.Globalization,ContentType=WindowsRuntime]\n" +
                "$awaiter = [WindowsRuntimeSystemExtensions].GetMember('GetAwaiter').Where({ $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' }, 'First')[0]\n" +
                "function Await($op, $type) { $awaiter.MakeGenericMethod($type).Invoke($null, @($op)).GetResult() }\n" +
                "$eng = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()\n" +
                "if ($eng -eq $null) { try { $eng = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage((New-Object Windows.Globalization.Language('fr-FR'))) } catch { } }\n" +
                "if ($eng -eq $null) { try { $eng = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage((New-Object Windows.Globalization.Language('en-US'))) } catch { } }\n" +
                "if ($eng -eq $null) { throw 'NOLANG' }\n" +
                "$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync('" + p + "')) ([Windows.Storage.StorageFile])\n" +
                "$stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])\n" +
                "$dec = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])\n" +
                "$bmp = $null\n" +
                "if ($dec.PixelWidth -lt 2400) {\n" +
                "  try {\n" +
                "    $tr = New-Object Windows.Graphics.Imaging.BitmapTransform\n" +
                "    $tr.ScaledWidth = [uint32]($dec.PixelWidth * 2)\n" +
                "    $tr.ScaledHeight = [uint32]($dec.PixelHeight * 2)\n" +
                "    $bmp = Await ($dec.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied, $tr, [Windows.Graphics.Imaging.ExifOrientationMode]::IgnoreExifOrientation, [Windows.Graphics.Imaging.ColorManagementMode]::DoNotColorManage)) ([Windows.Graphics.Imaging.SoftwareBitmap])\n" +
                "  } catch { $bmp = $null }\n" +
                "}\n" +
                "if ($bmp -eq $null) { $bmp = Await ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap]) }\n" +
                "$res = Await ($eng.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])\n" +
                "foreach ($l in $res.Lines) { Write-Output ('L:' + $l.Text) }\n" +
                "} catch { Write-Output ('E:' + $_.Exception.Message) }\n";
        }

        static void Run(string path, Job job)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(path)));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, WindowStyle = ProcessWindowStyle.Hidden,
            };
            string output, err = "";
            using (var p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();   // lu en parallele : sinon un long message d'erreur bloquerait le programme
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(45000))
                {
                    try { p.Kill(); } catch (Exception) { }
                    job.error = "Delai depasse (la lecture du texte a pris plus de 45 s)";
                    return;
                }
                output = outTask.Result;
                err = errTask.Result;
            }
            string msg = "";
            foreach (string raw in output.Split('\n'))
            {
                string l = raw.TrimEnd('\r');
                if (l.StartsWith("L:")) { string t = l.Substring(2).Trim(); if (t.Length > 0) job.lines.Add(t); }
                else if (l.StartsWith("E:")) msg = l.Substring(2).Trim();
            }
            if (msg.Length > 0)
            {
                job.error = msg == "NOLANG"
                    ? "Aucune langue avec reconnaissance de texte dans Windows (Parametres > Heure et langue > Langue : ajoute le francais)"
                    : "Lecture impossible : " + (msg.Length > 160 ? msg.Substring(0, 160) : msg);
                return;
            }
            if (job.lines.Count == 0 && err.Trim().Length > 0)
            {
                err = err.Trim();
                job.error = "Lecture impossible : " + (err.Length > 160 ? err.Substring(0, 160) : err);
            }
        }
    }
}
