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
            public string info = "";   // taille de l'image lue et langue de reconnaissance
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

        // entete commun : environnement, moteur de reconnaissance, fonctions Await et Prep (image agrandie ou reduite pour rester sous la taille maximale du moteur)
        static string Head()
        {
            return
                "$ProgressPreference = 'SilentlyContinue'\n" +
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
                "$mx = [double][Windows.Media.Ocr.OcrEngine]::MaxImageDimension\n" +
                "$global:cf = 1.0\n" +
                "$global:ow = 0\n" +
                "$global:oh = 0\n" +
                "function Prep($path) {\n" +
                "  $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])\n" +
                "  $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])\n" +
                "  $dec = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])\n" +
                "  $global:ow = $dec.PixelWidth\n" +
                "  $global:oh = $dec.PixelHeight\n" +
                "  $big = [double][Math]::Max($dec.PixelWidth, $dec.PixelHeight)\n" +
                "  $want = 1.0\n" +
                "  if ($big -lt 1800) { $want = 2.0 }\n" +
                "  $f = [Math]::Min($want, ($mx - 8) / $big)\n" +
                "  $bmp = $null\n" +
                "  if ([Math]::Abs($f - 1.0) -gt 0.02) {\n" +
                "    try {\n" +
                "      $tr = New-Object Windows.Graphics.Imaging.BitmapTransform\n" +
                "      $tr.ScaledWidth = [uint32]([Math]::Floor($dec.PixelWidth * $f))\n" +
                "      $tr.ScaledHeight = [uint32]([Math]::Floor($dec.PixelHeight * $f))\n" +
                "      $bmp = Await ($dec.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied, $tr, [Windows.Graphics.Imaging.ExifOrientationMode]::IgnoreExifOrientation, [Windows.Graphics.Imaging.ColorManagementMode]::DoNotColorManage)) ([Windows.Graphics.Imaging.SoftwareBitmap])\n" +
                "    } catch { $bmp = $null }\n" +
                "  }\n" +
                "  if ($bmp -eq $null) { $f = 1.0; $bmp = Await ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap]) }\n" +
                "  $global:cf = $f\n" +
                "  return $bmp\n" +
                "}\n";
        }

        // ------------------------------------------------------------------ lecture avec la position des mots (planning, plusieurs images)
        public class Word { public string text = ""; public float x, y, w, h; }
        public class Line { public List<Word> words = new List<Word>(); }
        /// <summary>Une image lue : sa taille d'origine et ses lignes de mots (positions en pixels de l'image d'origine).</summary>
        public class Page { public int w, h; public string error = ""; public List<Line> lines = new List<Line>(); }
        public class BoxJob
        {
            public volatile bool done;
            public string error = "";
            public string info = "";
            public List<Page> pages = new List<Page>();
        }

        /// <summary>Lit plusieurs images d'un coup (un seul lancement de PowerShell) et rend, pour chacune, ses mots et leur position. Les fichiers sont supprimes ensuite.</summary>
        public static BoxJob StartBoxes(List<string> paths)
        {
            var job = new BoxJob();
            var copy = new List<string>(paths);
            Task.Run(() =>
            {
                try { RunBoxes(copy, job); }
                catch (Exception e) { job.error = e.Message; }
                finally
                {
                    foreach (string f in copy) { try { File.Delete(f); } catch (Exception) { } }
                    job.done = true;
                }
            });
            return job;
        }

        static void RunBoxes(List<string> paths, BoxJob job)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(BoxScript(paths)));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " + enc)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, WindowStyle = ProcessWindowStyle.Hidden,
            };
            string output, err;
            int exit;
            using (var p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(30000 + 12000 * paths.Count))
                {
                    try { p.Kill(); } catch (Exception) { }
                    job.error = "Delai depasse (la lecture du texte a pris trop de temps)";
                    return;
                }
                output = outTask.Result;
                err = errTask.Result;
                exit = p.ExitCode;
            }
            Page page = null;
            Line line = null;
            string msg = "";
            foreach (string raw in output.Split('\n'))
            {
                string l = raw.TrimEnd('\r');
                if (l.StartsWith("N:")) { page = new Page(); job.pages.Add(page); line = null; }   // une entree par image, meme si sa lecture echoue
                else if (l.StartsWith("P:") && page != null)
                {
                    string[] wh = l.Substring(2).Trim().Split('x');
                    int pw, ph;
                    if (wh.Length == 2 && int.TryParse(wh[0], out pw) && int.TryParse(wh[1], out ph)) { page.w = pw; page.h = ph; }
                }
                else if (l.StartsWith("L:") && page != null) { line = new Line(); page.lines.Add(line); }
                else if (l.StartsWith("W:") && page != null && line != null)
                {
                    string[] f = l.Substring(2).Split(new[] { ' ' }, 5);
                    int x, y, w, h;
                    if (f.Length == 5 && int.TryParse(f[0], out x) && int.TryParse(f[1], out y) && int.TryParse(f[2], out w) && int.TryParse(f[3], out h))
                    {
                        string t = f[4].Trim();
                        if (t.Length > 0) line.words.Add(new Word { text = t, x = x, y = y, w = w, h = h });
                    }
                }
                else if (l.StartsWith("X:") && page != null) page.error = l.Substring(2).Trim();
                else if (l.StartsWith("I:")) job.info = l.Substring(2).Trim();
                else if (l.StartsWith("E:")) msg = l.Substring(2).Trim();
            }
            if (msg.Length > 0)
            {
                job.error = msg == "NOLANG"
                    ? "Aucune langue avec reconnaissance de texte dans Windows (Parametres > Heure et langue > Langue : ajoute le francais)"
                    : "Lecture impossible : " + (msg.Length > 160 ? msg.Substring(0, 160) : msg);
                return;
            }
            err = err.Trim();
            if (job.pages.Count == 0 && err.Length > 0 && !err.StartsWith("#< CLIXML") && exit != 0)
                job.error = "Lecture impossible : " + (err.Length > 160 ? err.Substring(0, 160) : err);
        }

        static string Script(string path)
        {
            string p = path.Replace("'", "''");
            return Head() +
                "$bmp = Prep '" + p + "'\n" +
                "try { Write-Output ('I:' + $bmp.PixelWidth + 'x' + $bmp.PixelHeight + ' ' + $eng.RecognizerLanguage.DisplayName) } catch { }\n" +
                "$res = Await ($eng.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])\n" +
                "foreach ($l in $res.Lines) { Write-Output ('L:' + $l.Text) }\n" +
                "} catch { Write-Output ('E:' + $_.Exception.Message) }\n";
        }

        // plusieurs images, avec la position de chaque mot (en pixels de l'image d'origine)
        static string BoxScript(List<string> paths)
        {
            var sb = new StringBuilder();
            sb.Append(Head());
            sb.Append("try { Write-Output ('I:' + $eng.RecognizerLanguage.DisplayName) } catch { }\n");
            foreach (string path in paths)
            {
                string p = path.Replace("'", "''");
                sb.Append("Write-Output 'N:'\n");
                sb.Append("try {\n");
                sb.Append("  $bmp = Prep '" + p + "'\n");
                sb.Append("  Write-Output ('P:' + $global:ow + 'x' + $global:oh)\n");
                sb.Append("  $res = Await ($eng.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])\n");
                sb.Append("  foreach ($l in $res.Lines) {\n");
                sb.Append("    Write-Output 'L:'\n");
                sb.Append("    foreach ($wd in $l.Words) {\n");
                sb.Append("      $r = $wd.BoundingRect\n");
                sb.Append("      Write-Output ('W:' + [int]($r.X / $global:cf) + ' ' + [int]($r.Y / $global:cf) + ' ' + [int]($r.Width / $global:cf) + ' ' + [int]($r.Height / $global:cf) + ' ' + $wd.Text)\n");
                sb.Append("    }\n");
                sb.Append("  }\n");
                sb.Append("} catch { Write-Output ('X:' + $_.Exception.Message) }\n");
            }
            sb.Append("} catch { Write-Output ('E:' + $_.Exception.Message) }\n");
            return sb.ToString();
        }

        static void Run(string path, Job job)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(path)));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " + enc)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, WindowStyle = ProcessWindowStyle.Hidden,
            };
            string output, err = "";
            int p0Exit = 0;
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
                p0Exit = p.ExitCode;
            }
            string msg = "";
            foreach (string raw in output.Split('\n'))
            {
                string l = raw.TrimEnd('\r');
                if (l.StartsWith("L:")) { string t = l.Substring(2).Trim(); if (t.Length > 0) job.lines.Add(t); }
                else if (l.StartsWith("I:")) job.info = l.Substring(2).Trim();
                else if (l.StartsWith("E:")) msg = l.Substring(2).Trim();
            }
            if (msg.Length > 0)
            {
                job.error = msg == "NOLANG"
                    ? "Aucune langue avec reconnaissance de texte dans Windows (Parametres > Heure et langue > Langue : ajoute le francais)"
                    : "Lecture impossible : " + (msg.Length > 160 ? msg.Substring(0, 160) : msg);
                return;
            }
            // le flux d'erreurs de PowerShell contient souvent du bruit « #< CLIXML » (avancement) qui n'est pas une erreur : on ne le prend jamais pour un echec
            err = err.Trim();
            if (job.lines.Count == 0 && err.Length > 0 && !err.StartsWith("#< CLIXML") && p0Exit != 0)
                job.error = "Lecture impossible : " + (err.Length > 160 ? err.Substring(0, 160) : err);
        }
    }
}
