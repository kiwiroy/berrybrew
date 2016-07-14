using Microsoft.Win32;
using System;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;

namespace BerryBrew
{
    public class Berrybrew
    {

        // sends a setting change message to reconfigure PATH

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, 
            int Msg, 
            IntPtr wParam, 
            string lParam, 
            uint fuFlags, 
            uint uTimeout, 
            IntPtr 
            lpdwResult
        );
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private const int WM_SETTINGCHANGE = 0x001a;
        private const int SMTO_ABORTIFHUNG = 0x2;

        public bool Debug { set; get; }

        static string assembly_path = Assembly.GetExecutingAssembly().Location;
        static string assembly_directory = Path.GetDirectoryName(assembly_path);       
        public Dirs Dir = new Dirs(assembly_directory);

        public Message Message = new Message();

        public Berrybrew()
        {
            // config

            dynamic json_conf = ParseConfig(Dir.Install);
            this.Dir.Add("root", json_conf.root_dir);
            this.Dir.Add("archive", json_conf.archive_dir);
            this.Debug = json_conf.debug;

            // messages

            dynamic json_messages = this.ParseJson("messages");
            foreach (dynamic entry in json_messages)
            {
                this.Message.Add(entry);
            }
        }

        internal static void AddBinToPath(string bin_path)
        {
            string path = PathGet();
            List<string> newPath = new List<string>();

            if (path == null)
            {
                newPath.Add(bin_path);
            }
            else
            {
                if (path[path.Length - 1] == ';')
                    path = path.Substring(0, path.Length - 1);

                newPath.Add(path);
                newPath.Add(bin_path);
            }

            PathSet(newPath);
        }

        internal static void AddPerlToPath(StrawberryPerl perl)
        {
            string path = PathGet();
            List<string> new_path = perl.Paths;
            new_path.Add(path);
            PathSet(new_path);
        }

        public void Available()
        {
            List<StrawberryPerl> perls = GatherPerls();
            
            this.Message.Print("available_header");

            StrawberryPerl current_perl = CheckWhichPerlInPath();
            string column_spaces = "               ";

            foreach (StrawberryPerl perl in perls)
            {
                // cheap printf
                string name_to_print = perl.Name + column_spaces.Substring(0, column_spaces.Length - perl.Name.Length);

                Console.Write("\t" + name_to_print);

                if (PerlInstalled(perl))
                    Console.Write(" [installed]");

                if (perl.Name == current_perl.Name)
                    Console.Write("*");

                Console.Write("\n");
            }
            this.Message.Print("available_footer");
        }

        internal StrawberryPerl CheckWhichPerlInPath()
        {
            string path = PathGet();

            StrawberryPerl current_perl = new StrawberryPerl();

            if (path != null)
            {
                string[] paths = path.Split(';');
                foreach (StrawberryPerl perl in GatherPerls())
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        if (paths[i] == perl.PerlPath
                            || paths[i] == perl.CPath
                            || paths[i] == perl.PerlSitePath)
                        {
                            current_perl = perl;
                            break;
                        }
                    }
                }
            }
            return current_perl;
        }

        public void Clean()
        {
            string archive_path = Dir.Archive;

            System.IO.DirectoryInfo archive_dir = new DirectoryInfo(archive_path);

            foreach (FileInfo file in archive_dir.GetFiles())
            {
                file.Delete();
            }
        }

        public void CompileExec(string parameters)
        {
            List<StrawberryPerl> perls_installed = GetInstalledPerls();
            List<StrawberryPerl> exec_with = new List<StrawberryPerl>();
            string command;

            if (parameters.StartsWith("--with"))
            {
                string param_list = Regex.Replace(parameters, @"--with\s+", "");

                string perl_str = param_list.Split(new[] { ' ' }, 2)[0];
                command = param_list.Split(new[] { ' ' }, 2)[1];

                string[] perls = perl_str.Split(',');

                foreach (StrawberryPerl perl in perls_installed)
                {
                    foreach (string perl_name in perls)
                    {
                        if (perl_name.Equals(perl.Name))
                            exec_with.Add(perl);
                    }
                }
            }
            else
            {
                command = parameters;
                exec_with = perls_installed;
            }

            string sys_path = PathGet();

            foreach (StrawberryPerl perl in exec_with)
            {
                Exec(perl, command, sys_path);
            }
        }

        public void Config()
        {
            string config_intro = this.Message.Get("config_intro");
            Console.WriteLine(config_intro + Version() + "\n");

            if (!ScanUserPath(new Regex("berrybrew.bin"))
                && !ScanSystemPath(new Regex("berrybrew.bin")))
            {
                this.Message.Print("add_bb_to_path");

                if (Console.ReadLine() == "y")
                {
                    AddBinToPath(Dir.Install);

                    if (ScanSystemPath(new Regex("berrybrew.bin")))
                    {
                        this.Message.Print("config_success");
                    }
                    else
                    {
                        this.Message.Print("config_failure");
                    }
                }
            }
            else
            {
                this.Message.Print("config_complete");
            }
        }
       
        internal static void Exec(StrawberryPerl perl, string command, string SysPath)
        {
            Console.WriteLine("Perl-" + perl.Name + "\n==============");

            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

            List<String> NewPath;
            NewPath = perl.Paths;
            NewPath.Add(SysPath);

            System.Environment.SetEnvironmentVariable("PATH", String.Join(";", NewPath));

            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/c " + perl.PerlPath + @"\" + command;
            process.StartInfo = startInfo;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();

            Console.WriteLine(process.StandardOutput.ReadToEnd());
            Console.WriteLine(process.StandardError.ReadToEnd());
            process.WaitForExit();
        }

        public void Extract(StrawberryPerl perl, string archive_path)
        {
            if (File.Exists(archive_path))
            {
                Console.WriteLine("Extracting " + archive_path);
                ExtractZip(archive_path, perl.InstallPath);
            }
        }

        internal static void ExtractZip(string archive_path, string outFolder)
        {
            ZipFile zf = null;
            try
            {
                FileStream fs = File.OpenRead(archive_path);
                zf = new ZipFile(fs);
                foreach (ZipEntry zipEntry in zf)
                {
                    if (!zipEntry.IsFile)
                    {
                        continue;           // Ignore directories
                    }
                    String entryFileName = zipEntry.Name;
                    // to remove the folder from the entry:- entryFileName = Path.GetFileName(entryFileName);
                    // Optionally match entrynames against a selection list here to skip as desired.
                    // The unpacked length is available in the zipEntry.Size property.

                    byte[] buffer = new byte[4096];     // 4K is optimum
                    Stream zipStream = zf.GetInputStream(zipEntry);

                    // Manipulate the output filename here as desired.
                    String fullZipToPath = Path.Combine(outFolder, entryFileName);
                    string directoryName = Path.GetDirectoryName(fullZipToPath);
                    if (directoryName.Length > 0)
                        Directory.CreateDirectory(directoryName);

                    // Unzip file in buffered chunks. This is just as fast as unpacking to a buffer the full size
                    // of the file, but does not waste memory.
                    // The "using" will close the stream even if an exception occurs.
                    using (FileStream streamWriter = File.Create(fullZipToPath))
                    {
                        StreamUtils.Copy(zipStream, streamWriter, buffer);
                    }
                }
            }
            finally
            {
                if (zf != null)
                {
                    zf.IsStreamOwner = true; // Makes close also shut the underlying stream
                    zf.Close(); // Ensure we release resources
                }
            }
        }

        public string Fetch(StrawberryPerl perl)
        {
            WebClient webClient = new WebClient();
            string archive_path = GetDownloadPath(perl);

            if (!File.Exists(archive_path))
            {
                Console.WriteLine("Downloading " + perl.Url + " to " + archive_path);
                webClient.DownloadFile(perl.Url, archive_path);
            }

            Console.WriteLine("Confirming checksum ...");
            using (var cryptoProvider = new SHA1CryptoServiceProvider())
            {
                using (var stream = File.OpenRead(archive_path))
                {
                    string hash = BitConverter.ToString(cryptoProvider.ComputeHash(stream)).Replace("-", "").ToLower();

                    if (perl.Sha1Checksum != hash)
                    {
                        Console.WriteLine("Error checksum of downloaded archive \n"
                            + archive_path
                            + "\ndoes not match expected output\nexpected: "
                            + perl.Sha1Checksum
                            + "\n     got: " + hash);
                        stream.Dispose();
                        Console.Write("Whould you like berrybrew to delete the corrupted download file? y/n [n]");
                        if (Console.ReadLine() == "y")
                        {
                            string retval = RemoveFile(archive_path);
                            if (retval == "True")
                            {
                                Console.WriteLine("Deleted! Try to install it again!");
                            }
                            else
                            {
                                Console.WriteLine("Unable to delete " + archive_path);
                            }
                        }
                        Environment.Exit(0);
                    }
                }
            }
            return archive_path;
        }

        internal List<StrawberryPerl> GatherPerls()
        {
            List<StrawberryPerl> perls = new List<StrawberryPerl>();
            var json_list = ParseJson("perls");

            foreach (var version in json_list)
            {
                perls.Add(
                    new StrawberryPerl(
                        Dir,
                        version.name,
                        version.file,
                        version.url,
                        version.ver,
                        version.csum
                    )
                );
            }
            return perls;
        }

        internal static string GetDownloadPath(StrawberryPerl perl)
        {
            string path;

            try
            {
                if (!Directory.Exists(perl.ArchivePath))
                    Directory.CreateDirectory(perl.ArchivePath);

                return perl.ArchivePath + @"\" + perl.ArchiveName;
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Error, do not have permissions to create directory: " + perl.ArchivePath);
            }

            Console.WriteLine("Creating temporary directory instead");
            do
            {
                path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            } while (Directory.Exists(path));
 
            Directory.CreateDirectory(path);

            return path + @"\" + perl.ArchiveName;
        }

        internal List<StrawberryPerl> GetInstalledPerls()
        {
            List<StrawberryPerl> perls = GatherPerls();
            List<StrawberryPerl> PerlsInstalled = new List<StrawberryPerl>();

            foreach (StrawberryPerl perl in perls)
            {
                if (PerlInstalled(perl))
                    PerlsInstalled.Add(perl);
            }
            return PerlsInstalled;
        }



        public void Off()
        {
            RemovePerlFromPath();
            Console.Write("berrybrew perl disabled. Open a new shell to use system perl\n");
        }
 
        public static dynamic ParseConfig(string install_dir)
        {
            string filename = "config.json";
            string json_path = String.Format("{0}/data/{1}", install_dir, filename);
            string json_file = Regex.Replace(json_path, @"bin", "");

            try
            {
                using (StreamReader r = new StreamReader(json_file))
                {
                    string json = r.ReadToEnd();

                    try
                    {
                        dynamic json_list = JsonConvert.DeserializeObject(json);
                        return json_list;
                    }
                    catch (JsonReaderException error)
                    {
                        Console.WriteLine("\n{0} file is malformed. See berrybrew_error.txt in this directory for details.", json_file);
                        using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"berrybrew_error.txt", true))
                        {
                            file.WriteLine(error);
                        }
                        Environment.Exit(0);
                    }
                }
            }
            catch (System.IO.FileNotFoundException)
            {
                Console.WriteLine("\n{0} file can not be found in {1}", filename, install_dir);
                Environment.Exit(0);
            }
            return "";
        }       

        internal dynamic ParseJson(string type)
        {
            string install_dir = Dir.Install;
            string filename = String.Format("{0}.json", type);
            string json_path = String.Format("{0}/data/{1}", install_dir, filename);
            string json_file = Regex.Replace(json_path, @"bin", "");

            try
            {
                using (StreamReader r = new StreamReader(json_file))
                {
                    string json = r.ReadToEnd();

                    try
                    {
                        dynamic json_list = JsonConvert.DeserializeObject(json);
                        return json_list;
                    }
                    catch (JsonReaderException error)
                    {
                        Console.WriteLine("\n{0} file is malformed. See berrybrew_error.txt in this directory for details.", json_file);
                        using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"berrybrew_error.txt", true))
                        {
                            file.WriteLine(error);
                        }
                        Environment.Exit(0);
                    }
                }
            }
            catch (System.IO.FileNotFoundException)
            {
                Console.WriteLine("\n{0} file can not be found in {1}", filename, install_dir);
                Environment.Exit(0);
            }
            return "";
        }

        internal static string PathGet()
        {
            string keyName = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment\";
            string path = (string)Registry.LocalMachine.OpenSubKey(keyName).GetValue(
                "PATH",
                "",
                RegistryValueOptions.DoNotExpandEnvironmentNames
            );
            return path;
        }
        
        internal static void PathSet(List<string> path)
        {
            path.RemoveAll(str => String.IsNullOrEmpty(str));

            string keyName = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
            Registry.LocalMachine.CreateSubKey(keyName).SetValue(
                "Path", 
                String.Join(";", path), 
                RegistryValueKind.ExpandString
            );
            
            SendMessageTimeout(
                HWND_BROADCAST, 
                WM_SETTINGCHANGE, 
                IntPtr.Zero, 
                "Environment", 
                SMTO_ABORTIFHUNG, 
                100, 
                IntPtr.Zero
            );
        }

        internal static bool PerlInstalled(StrawberryPerl perl)
        {
            if (Directory.Exists(perl.InstallPath)
                && File.Exists(perl.PerlPath + @"\perl.exe"))
            {
                return true;
            }
            return false;
        }

        internal static string RemoveFile(string filename)
        {
            try
            {
                File.Delete(filename);
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
            return true.ToString();
        }

        private static void RemoveFilesystemAttributes(string currentDir)
        {
           if (Directory.Exists(currentDir))
           {
               string[] subDirs = Directory.GetDirectories(currentDir);
               foreach(string dir in subDirs)
               RemoveFilesystemAttributes(dir);
               string[] files = files = Directory.GetFiles(currentDir);
               foreach (string file in files)
               File.SetAttributes(file, FileAttributes.Normal);
           }
        }

        public void RemovePerl(string version_to_remove)
        {
            try
            {
                StrawberryPerl perl = ResolveVersion(version_to_remove);
                StrawberryPerl current_perl = CheckWhichPerlInPath();

                if (perl.Name == current_perl.Name)
                {
                    Console.WriteLine("Removing Perl " + version_to_remove + " from PATH");
                    RemovePerlFromPath();
                }

                if (Directory.Exists(perl.InstallPath))
                {
                    try
                    {
                        RemoveFilesystemAttributes(perl.InstallPath);
                        Directory.Delete(perl.InstallPath, true);
                        Console.WriteLine("Successfully removed Strawberry Perl " + version_to_remove);
                    }
                    catch (System.IO.IOException)
                    {
                        Console.WriteLine("Unable to completely remove Strawberry Perl " + version_to_remove + " some files may remain");
                    }
                }
                else
                {
                    Console.WriteLine("Strawberry Perl " + version_to_remove + " not found (are you sure it's installed?");
                    Environment.Exit(0);
                }
            }
            catch (ArgumentException)
            {
                this.Message.Print("perl_unknown_version");
                Environment.Exit(0);
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Unable to remove Strawberry Perl " + version_to_remove + " permission was denied by System");
            }
        }

        internal void RemovePerlFromPath()
        {
            string path = PathGet();
            List<String> paths = new List<String>();

            if (path != null)
            {
                paths = path.Split(';').ToList();
                foreach (StrawberryPerl perl in GatherPerls())
                {
                    for (var i = 0; i < paths.Count; i++)
                    {
                        if (paths[i] == perl.PerlPath
                            || paths[i] == perl.CPath
                            || paths[i] == perl.PerlSitePath)
                        {
                            paths[i] = "";
                        }
                    }
                }

                PathSet(paths);
            }
        }

        public StrawberryPerl ResolveVersion(string version_to_resolve)
        {
            foreach (StrawberryPerl perl in GatherPerls())
            {
                if (perl.Name == version_to_resolve)
                    return perl;
            }
            throw new ArgumentException("Unknown version: " + version_to_resolve);
        }

        internal static bool ScanSystemPath(Regex bin_pattern)
        {
            string system_path = Environment.GetEnvironmentVariable("path", EnvironmentVariableTarget.Machine);

            if (system_path != null)
            {
                foreach (string sys_p in system_path.Split(';'))
                {
                    if (bin_pattern.Match(sys_p).Success)
                        return true;
                }
            }
            return false;
        }

        internal static bool ScanUserPath(Regex bin_pattern)
        {
            string user_path = Environment.GetEnvironmentVariable("path", EnvironmentVariableTarget.User);
            if (user_path != null)
            {
                foreach (string user_p in user_path.Split(';'))
                {
                    if (bin_pattern.Match(user_p).Success)
                        return true;
                }
            }
            return false;
        }

        public void Switch(string version_to_switch)
        {
            try
            {
                StrawberryPerl perl = ResolveVersion(version_to_switch);

                // if Perl version not installed, can't switch
                if (!PerlInstalled(perl))
                {
                    Console.WriteLine("Perl version " + perl.Name + " is not installed. Run the command:\n\n\tberrybrew install " + perl.Name);
                    Environment.Exit(0);
                }

                RemovePerlFromPath();
                AddPerlToPath(perl);

                Console.WriteLine("Switched to " + version_to_switch + ", start a new terminal to use it.");
            }
            catch (ArgumentException)
            {
                this.Message.Print("perl_unknown_version");
                Environment.Exit(0);
            }
        }

        public string Version()
        {
            return this.Message.Get("version");
        }
    }

    public class Message
    {
        public Hashtable msgMap = new Hashtable();

        public string Get(string label)
        {
            return this.msgMap[label].ToString();
        }

        public void Print(string label)
        {
            string msg = this.Get(label);
            Console.WriteLine(msg);
        }

        public void Add(dynamic json)
        {
            string content = null;

            foreach (string line in json.content)
            {
                content += String.Format("{0}\n", line);
            }
            this.msgMap.Add(json.label.ToString(), content);
        }
    }

    public class Dirs 
    {
        public string Install; // berrybrew location
        public string Root;    // strawberry base location
        public string Archive; // zip location
        
        public void Add(string name, dynamic dir)
        {
            if (name == "root")
            {
                this.Root = dir + "\\"; 
            }
            if (name == "archive")
            {
                this.Archive = dir;
            }
        }
        
        public Dirs(string install_dir)
        {
            this.Install = install_dir;
        }
    }

    public struct StrawberryPerl
    {
        public string Name;
        public string ArchiveName;
        public string Url;
        public string Version;
        public string ArchivePath;
        public string InstallPath;
        public string CPath;
        public string PerlPath;
        public string PerlSitePath;
        public List<String> Paths;
        public string Sha1Checksum;

        public StrawberryPerl(Dirs Dir, object name, object archive, object url, object version, object csum)
        {
            this.Name = name.ToString();
            this.ArchiveName = archive.ToString();
            this.Url = url.ToString();
            this.Version = version.ToString();
            this.ArchivePath = Dir.Archive;
            this.InstallPath =  Dir.Root+ name;
            this.CPath = Dir.Root+ name + @"\c\bin";
            this.PerlPath = Dir.Root+ name + @"\perl\bin";
            this.PerlSitePath = Dir.Root+ name + @"\perl\site\bin";
            this.Paths = new List <String>{
                this.CPath, this.PerlPath, this.PerlSitePath
            };
            this.Sha1Checksum = csum.ToString();
        }
    }
}
