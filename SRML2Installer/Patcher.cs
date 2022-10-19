using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using Colorful = Colorful.Console;
using System.Drawing;

namespace SRML2Installer
{
	class Patcher : IDisposable
	{
		private AssemblyDefinition curAssembly;
		private MethodDefinition target;
		private MethodReference methodToPatchIn;
		private String fileName;
		public Patcher(string fileName, MethodReference methodToPatchIn)
		{
			Init(fileName);
			this.methodToPatchIn = methodToPatchIn;
			this.fileName = fileName;
		}

		/// <summary>
		/// The initialization point for assembly resolver.
		/// Also fires FindTarget(); -> MethodDefinition
		/// </summary>
		/// <param name="fileName"></param>
		/// <exception cref="Exception"></exception>
		private void Init(string fileName)
		{
			try
			{
				var resolver = new DefaultAssemblyResolver();
				resolver.AddSearchDirectory(Path.GetDirectoryName(fileName));
				curAssembly = AssemblyDefinition.ReadAssembly(fileName, new ReaderParameters() { AssemblyResolver = resolver });
			}
			catch
			{
				throw new Exception($"Couldn't find {fileName}!");
			}
			target = FindTarget();
		}

		/// <summary>
		/// Finds game context target.
		/// </summary>
		/// <returns></returns>
		MethodDefinition FindTarget()
		{
			return curAssembly.MainModule.GetType("GameContext").Methods.First((x) => x.Name == "Awake");
		}

		/// <summary>
		/// Checks for IL2CPP patch file.
		/// </summary>
		/// <returns></returns>
		public bool IsPatched()
		{
			return target.Body.Instructions[0].OpCode == OpCodes.Call && target.Body.Instructions[0].Operand is MethodReference methodRef && (methodRef.Name == "LoadSR2ModLoader" || methodRef.Name == "Preload");
		}

		/// <summary>
		/// Removes old IL2CPP patch // Current update on 4.9 - 5.0
		/// </summary>
		public void RemoveOldPatch()
		{
			if (target.Body.Instructions[0].Operand is MethodReference h && h.Name == "LoadSRML2") target.Body.GetILProcessor().Remove(target.Body.Instructions[0]);
			if (target.DeclaringType.Methods.FirstOrDefault((x) => x.Name == "LoadSRML2") is MethodDefinition d)
			{
				target.DeclaringType.Methods.Remove(d);
			}
		}

		/// <summary>
		/// Holds a bunch of yield returns to create OpCodes
		/// Sets loading instructions for code execution.
		/// </summary>
		/// <param name="proc"></param>
		/// <param name="modDef"></param>
		/// <returns></returns>
		IEnumerable<Instruction> GetLoadingInstructions(ILProcessor proc, ModuleDefinition modDef)
		{
			yield return proc.Create(OpCodes.Ldstr, "SRML2/Libs");
			yield return proc.Create(OpCodes.Ldstr, "*.dll");
			yield return proc.Create(OpCodes.Ldc_I4_1);

			var method = typeof(Directory).GetMethods()
				.First((x) => x.Name == "GetFiles" && x.GetParameters().Length == 3);
			var imported = modDef.ImportReference(method);
			yield return proc.Create(OpCodes.Call, imported);

			// Platform-Specific Assembly Code
			yield return proc.Create(OpCodes.Stloc_0);
			yield return proc.Create(OpCodes.Ldc_I4_0);
			yield return proc.Create(OpCodes.Stloc_1);
			var seventeen = proc.Create(OpCodes.Ldloc_1);
			yield return proc.Create(OpCodes.Br_S, seventeen);
			var eight = proc.Create(OpCodes.Ldloc_0);
			yield return eight;
			yield return proc.Create(OpCodes.Ldloc_1);
			yield return proc.Create(OpCodes.Ldelem_Ref);
			yield return proc.Create(OpCodes.Call, modDef.ImportReference(typeof(Assembly).GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static, Type.DefaultBinder, new Type[] { typeof(string) }, null)));
			yield return proc.Create(OpCodes.Pop);
			yield return proc.Create(OpCodes.Ldloc_1);
			yield return proc.Create(OpCodes.Ldc_I4_1);
			yield return proc.Create(OpCodes.Add);
			yield return proc.Create(OpCodes.Stloc_1);
			yield return seventeen;
			yield return proc.Create(OpCodes.Ldloc_0);
			yield return proc.Create(OpCodes.Ldlen);
			yield return proc.Create(OpCodes.Conv_I4);
			yield return proc.Create(OpCodes.Blt_S, eight);
		}

		/// <summary>
		/// Adds loading method for the mod framework.
		/// </summary>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		public MethodDefinition AddLoadMethod()
		{
			if (target.DeclaringType.Methods.FirstOrDefault((x) => x.Name == "LoadSRModLoader2") is MethodDefinition d)
				return d;

			var method = new MethodDefinition("LoadSRModLoader2", MethodAttributes.Public | MethodAttributes.Static, target.Module.TypeSystem.Void);
			method.Body.Variables.Add(new VariableDefinition(curAssembly.MainModule.ImportReference(typeof(string[]))));
			method.Body.Variables.Add(new VariableDefinition(curAssembly.MainModule.TypeSystem.Int32));
			var proc = method.Body.GetILProcessor();
			var mainCall = target.Body.GetILProcessor().Create(OpCodes.Call, curAssembly.MainModule.ImportReference(methodToPatchIn));
			if (!curAssembly.MainModule.TryGetTypeReference("UnityEngine.Debug", out var reference)) throw new Exception("Couldn't find UnityEngine.Debug!");
			var logRef = new MethodReference("Log", curAssembly.MainModule.TypeSystem.Void, reference);
			logRef.Parameters.Add(new ParameterDefinition(curAssembly.MainModule.TypeSystem.Object));
			var onFailWrite = proc.Create(OpCodes.Call, logRef);

			if (!curAssembly.MainModule.TryGetTypeReference("UnityEngine.Application", out var quitRef)) throw new Exception("Couldn't find UnityEngine.Application!");

			var applicationQuit = proc.Create(OpCodes.Call, new MethodReference("Quit", curAssembly.MainModule.TypeSystem.Void, quitRef));

			var ret = proc.Create(OpCodes.Ret);
			var leave = proc.Create(OpCodes.Leave, ret);

			var mainRet = proc.Create(OpCodes.Ret);

			foreach (var v in GetLoadingInstructions(proc, curAssembly.MainModule))
			{
				proc.Append(v);
			}

			proc.Append(mainCall);

			proc.InsertAfter(mainCall, mainRet);
			proc.InsertAfter(mainRet, onFailWrite);
			proc.InsertAfter(onFailWrite, applicationQuit);
			proc.InsertAfter(applicationQuit, leave);
			proc.InsertAfter(leave, ret);

			var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
			{
				TryStart = method.Body.Instructions.First(),
				TryEnd = onFailWrite,
				HandlerStart = onFailWrite,
				HandlerEnd = ret,
				CatchType = curAssembly.MainModule.ImportReference(typeof(Exception)),
			};

			method.Body.ExceptionHandlers.Add(handler);
			target.DeclaringType.Methods.Add(method);

			return method;
		}

		/// <summary>
		/// Creates the patch file using the ILProcessor.
		/// </summary>
		public void Patch()
		{
			RemoveOldPatch();
			var proc = target.Body.GetILProcessor();
			proc.InsertBefore(target.Body.Instructions[0], proc.Create(OpCodes.Call, AddLoadMethod()));
		}

		/// <summary>
		/// Unpatches the patched file.
		/// </summary>
		public void Unpatch()
		{
			if (target.Body.Instructions[0].Operand is MethodReference h && h.Name == "LoadSRModLoader2") target.Body.GetILProcessor().Remove(target.Body.Instructions[0]);
			if (target.DeclaringType.Methods.FirstOrDefault((x) => x.Name == "LoadSRModLoader2") is MethodDefinition d)
			{
				target.DeclaringType.Methods.Remove(d);
			}
			RemoveOldPatch();
		}

		/// <summary>
		/// Checks if path exists. if not will return false.
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		bool CheckOrDelete(string path)
		{
			if (!File.Exists(path)) return true;
			Console.Write($"Found {Path.GetFileName(path)} in target directory! Delete? (y/n): ");
			if (Console.ReadLine() == "y")
			{
				Console.WriteLine("Deleting and continuing...");
				File.Delete(path);
				return true;
			}
			Console.WriteLine("Aborting...", Color.Red);
			return false;
		}

		/// <summary>
		/// Saves the path and file loaded in the _Data folder.
		/// </summary>
		/// <exception cref="Exception"></exception>
		public void Save()
		{
			var pathRoot = Path.GetDirectoryName(fileName);
			string patchedName = Path.Combine(pathRoot, Path.GetFileNameWithoutExtension(fileName) + "_patched.dll");
			string oldName = Path.Combine(pathRoot, Path.GetFileNameWithoutExtension(fileName) + "_old.dll");
			if (!CheckOrDelete(patchedName) || !CheckOrDelete(oldName)) throw new Exception("Cannot continue installation while the file exists!");
			curAssembly.Write(patchedName);
			Dispose();
			File.Move(fileName, oldName);
			File.Move(patchedName, fileName);
		}

		/// <summary>
		/// Regular dispose function for Module and Assembly.
		/// </summary>
		public void Dispose()
		{
			curAssembly?.Dispose();
			methodToPatchIn?.Module?.Assembly?.Dispose();
		}

		/// <summary>
		/// Uninstalls the old patch and replaces with a new patch.
		/// </summary>
		/// <exception cref="Exception"></exception>
		public void Uninstall()
		{
			var pathRoot = Path.GetDirectoryName(fileName);
			string oldName = Path.Combine(pathRoot, Path.GetFileNameWithoutExtension(fileName) + "_old.dll");
			string patchedName = Path.Combine(pathRoot, Path.GetFileNameWithoutExtension(fileName) + "_patched.dll");
			if (!File.Exists(oldName))
			{
				Console.WriteLine($"Couldn't find old {Path.GetFileName(fileName)}!");
				Console.WriteLine("Attempting forceful uninstallation...", Color.Yellow); // Yellow = Trying
				Unpatch();
				if (!CheckOrDelete(patchedName)) throw new Exception();
				curAssembly.Write(patchedName);
				Dispose();
				File.Delete(fileName);
				File.Move(patchedName, fileName);
			}
			else
			{
				Dispose();
				File.Delete(fileName);
				Init(oldName);
				Unpatch();
				curAssembly.Write(fileName);
				Dispose();
				File.Delete(oldName);
			}
			Console.WriteLine("Unpatching Successfull!", Color.Green);
		}
	}
}
