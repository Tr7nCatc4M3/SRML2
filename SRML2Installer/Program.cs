using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Reflection;
using Mono.Cecil;

namespace SRML2Installer
{
	class Program
	{
		public const String SRML2 = "SRML2.dll";
		public const String embeddedResourcePath = "SRML2Installer.Libs.";
		public const String embeddedResourceProject = "SRML2Installer.";
		static void Main(string[] args)
		{
			bool vortexMode = args.Length > 0 && args[0].StartsWith("-v");
			bool vortexUninstall = vortexMode && args[0] == "-vu";
			try
			{
				string fileName = "";
				if (!vortexMode && args.Length == 0)
					fileName = GameFinder.FindGame();
				else
					fileName = (vortexMode ? args[1] : args[0]) + "/SlimeRancher2_Data/Managed/Assembly-CSharp.dll";
				string root = Path.GetDirectoryName(fileName);
				var srml2Path = Path.Combine(root, SRML2);

				Console.WriteLine();

				bool uninstalling = false;
				bool alreadyPatched = false;
				bool didSRML2Exist = File.Exists(srml2Path);

				try_to_patch:
				if (File.Exists(srml2Path))
				{
					var patcher = new Patcher(fileName, GetOnLoad(srml2Path));
					if (patcher.IsPatched())
					{
						alreadyPatched = true;
						if (!vortexMode)
						{
							Console.Write($"Game is already patched! Would you like to uninstall? (selecting n will trigger a update) (y/n): ");

						poll_user:
							var response = Console.ReadLine();
							if (response == "yes" || response == "y")
							{
								uninstalling = true;
								patcher.Uninstall();
							}
							else if (response == "n" || response == "no")
							{
							}
							else
							{
								Console.Write("Please enter a valid option! (y/n): ");
								goto poll_user;
							}
						}
						if (vortexUninstall)
						{
							uninstalling = true;
							patcher.Uninstall();
						}
					}
					else
					{
						Console.WriteLine("Patching...");
						patcher.Patch();
						Console.WriteLine("Patching Successfull!");
						patcher.Save();
					}
					patcher.Dispose();
					SendFilesOver(didSRML2Exist);
				}
				else
				{
					SendFilesOver();
					goto try_to_patch;
				}

				string GetAlternateRoot()
				{
					string alternateRoot = Path.Combine(Directory.GetParent(root).Parent.FullName, "SRML2", "Libs");
					if (!Directory.Exists(alternateRoot))
					{
						Directory.CreateDirectory(alternateRoot);
					}
					return alternateRoot;
				}

				void SendFilesOver(bool canLog = true)
				{
					foreach (var file in Directory.GetFiles(GetAlternateRoot())) File.Delete(file);

					foreach(var v in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where((x) => x.Length > embeddedResourceProject.Length && x.Substring(0, embeddedResourceProject.Length) == embeddedResourceProject))
					{
						var file = v.Substring(embeddedResourcePath.Length);
						var combine = Path.Combine(file.Contains("SRML2") ? root : GetAlternateRoot(), file);
						if (File.Exists(combine))
						{
							if (canLog)
								if (!uninstalling) Console.WriteLine($"Found old {file}! Replacing...");
								else Console.WriteLine($"Deleting {file}...");
							File.Delete(combine);
						}
						if (uninstalling) continue;
						var str = File.Create(combine);
						var otherStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Program), v.Substring(embeddedResourceProject.Length));
						otherStream.CopyTo(str);
						otherStream.Close();
						str.Close();
					}
					if (!uninstalling)
					{
						string umfPath = Path.Combine(Directory.GetParent(root).Parent.FullName, "uModFramework", "Lib", "net462");
						if (File.Exists(Path.Combine(umfPath, "0Harmony.dll")))
						{
							Console.WriteLine("Found existing UMF installation! Patching now...");
							File.Copy(Path.Combine(GetAlternateRoot(), "0Harmony.dll"), Path.Combine(umfPath, "0Harmony.dll"), true);
						}
					}
				}

				Console.WriteLine();

				string type = alreadyPatched ? "Update" : "Installation";
				string ending = alreadyPatched ? "" : $"(old assembly stored as {Path.GetFileNameWithoutExtension(fileName)}_old.dll)";

				if (!uninstalling)
				{
					Console.WriteLine($"{type} complete!" + ending);
					var modPath = Path.Combine(Directory.GetParent(root).Parent.FullName, "SRML2", "Mods");
					if (!Directory.Exists(modPath)) Directory.CreateDirectory(modPath);
					Console.WriteLine($"Mods can be installed at {modPath}");
				}
				else Console.WriteLine($"Uninstallation Complete!");
			}
			catch (UnauthorizedAccessException e)
			{
				Console.WriteLine(e.Message);
				Console.WriteLine($"Please run {Path.GetFileName(Assembly.GetExecutingAssembly().Location)} as an administrator!");
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}
			if (vortexMode) return;

			Console.Write("Press any key to continue...");
			Console.ReadKey();
			return;
		}

		static MethodReference GetOnLoad(string path)
		{
			AssemblyDefinition def = null;
			try
			{
				def = AssemblyDefinition.ReadAssembly(path);
			}
			catch
			{
				throw new Exception($"Couldn't find {SRML2}!");
			}
			return def.MainModule.GetType("SRML2.Main").Methods.First((x) => x.Name == "PreLoad");
		}
	}
}
