#region License
/* ****************************************************************************
 * Copyright (c) Llewellyn Pritchard. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. 
 * A copy of the license can be found in the License.html file at the root of this distribution. 
 * By using this source code in any fashion, you are agreeing to be bound by the terms of the 
 * Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 * ***************************************************************************/
#endregion

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Scripting.Math;
using System.Reflection;
using Microsoft.Scripting.Utils;
using System.Collections;
using Microsoft.Scripting;
using System.IO;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Actions;
using System.Diagnostics;
using IronScheme.Runtime.R6RS;

namespace IronScheme.Runtime
{
  public partial class Builtins
  {

    [Builtin("with-input-from-file")]
    public static object WithInputFromFile(object filename, object thunk)
    {
      ICallable f = RequiresNotNull<ICallable>(thunk);
      string fn = RequiresNotNull<string>(filename);

      TextReader old = currentinputport;

      try
      {
        using (TextReader r = File.OpenText(GetPath(fn)))
        {
          currentinputport = r;
          return f.Call();
        }
      }
      catch (FileNotFoundException ex)
      {
        return FileNotFoundViolation("with-input-from-file", ex.Message, filename);
      }
      catch (Condition)
      {
        throw;
      }
      catch (Exception ex)
      {
        return AssertionViolation("with-input-from-file", ex.Message, filename);
      }
      finally
      {
        if (readcache.ContainsKey(currentinputport))
        {
          readcache.Remove(currentinputport);
        }
        currentinputport = old;
      }
    }


    [Builtin("with-output-to-file")]
    public static object WithOutputToFile(object filename, object thunk)
    {
      ICallable f = RequiresNotNull<ICallable>(thunk);
      string fn = RequiresNotNull<string>(filename);

      TextWriter old = currentoutputport;

      try
      {
        using (TextWriter w = File.CreateText(GetPath(fn)))
        {
          currentoutputport = w;
          return f.Call();
        }
      }
      catch (FileNotFoundException ex)
      {
        return FileNotFoundViolation("with-output-to-file", ex.Message, filename);
      }
      catch (Condition)
      {
        throw;
      }
      catch (Exception ex)
      {
        return AssertionViolation("with-input-from-file", ex.Message, filename);
      }
      finally
      {
        currentoutputport = old;
      }
    }

    static Assembly AssemblyLoad(string path)
    {
      string fn = Path.GetFullPath(path);
      if (File.Exists(fn))
      {
        return Assembly.LoadFrom(fn);

      }
      FileNotFoundViolation(FALSE, "file not found", path);
      return null;
    }

    [Builtin("load")] // this is patched in r6rs mode, but its needed to bootstrap
    [Builtin("load-r5rs")]
    public static object Load(object filename)
    {
      CodeContext cc = IronScheme.Compiler.BaseHelper.cc;

      string path = GetPath(filename as string);

      switch (Path.GetExtension(path))
      {
        case ".exe":
        case ".dll":

          Assembly ext = AssemblyLoad(path);
          if (Attribute.IsDefined(ext, typeof(ExtensionAttribute)))
          {
            IronScheme.Compiler.Generator.AddGenerators(cc, ext);

            foreach (ExtensionAttribute ea in ext.GetCustomAttributes(typeof(ExtensionAttribute), false))
            {
              if (ea.BuiltinsType != null)
              {
                IronScheme.Compiler.Generator.AddBuiltins(cc, ea.BuiltinsType);
              }
              if (ea.ScriptResource != null)
              {
                //TODO: ExtensionAttribute.ScriptResource
              }
            }
          }
          else
          {
            // just reference.?
            MethodInfo entry = null;
            foreach (Type t in ext.GetExportedTypes())
            {
              if (t.BaseType == typeof(CustomSymbolDictionary))
              {
                List<Type> ii = new List<Type>(t.GetInterfaces());
                if (ii.Contains(typeof(IModuleDictionaryInitialization)))
                {
                  entry = t.GetMethod("Initialize");
                  if (entry != null)
                  {
                    break;
                  }
                }
              }
            }

            if (entry == null)
            {
              // what now?
            }
            else
            {
              IModuleDictionaryInitialization init = Activator.CreateInstance(entry.DeclaringType) as
                IModuleDictionaryInitialization;

              CodeContext ccc = new CodeContext(cc, init as IAttributesCollection);
              init.InitializeModuleDictionary(ccc);

              CallTargetWithContext0 t = Delegate.CreateDelegate(typeof(CallTargetWithContext0), entry) as CallTargetWithContext0;
              return t(ccc);
            }
          }
          break;
        default:

          // check for already compiled version
          string cfn = Path.ChangeExtension(path, ".dll");
          if (File.Exists(cfn))
          {
            DateTime ct = File.GetLastWriteTime(cfn);
            if (!File.Exists(path) || ct >= File.GetLastWriteTime(path))
            {
              if (File.GetLastWriteTime(Path.Combine(ApplicationDirectory, "IronScheme.dll")) <= ct || cfn.EndsWith("ironscheme.boot.dll"))
              {
                path = cfn;
                goto case ".dll";
              }
            }
          }

          if (!File.Exists(path))
          {
            return FileNotFoundViolation("load", "file not found", filename);
          }

          SourceUnit su = ScriptDomainManager.CurrentManager.Host.TryGetSourceFileUnit(cc.LanguageContext.Engine, path, Encoding.Default);
#if DEBUG
          Stopwatch sw = Stopwatch.StartNew();
#endif
          ScriptModule sm = ScriptDomainManager.CurrentManager.CompileModule(Path.GetFileNameWithoutExtension(path), su);

          Compiler.SimpleGenerator.ClearGlobals();
#if DEBUG
          Trace.WriteLine(sw.ElapsedMilliseconds, "Compile module: " + sm.FileName);
          sw = Stopwatch.StartNew();
#endif
          object result = sm.GetScripts()[0].Run(sm);
#if DEBUG
          Trace.WriteLine(sw.ElapsedMilliseconds, "Run script: " + sm.GetScripts()[0].SourceUnit);
#endif
          return result;

      }

      return Unspecified;
    }

    [Builtin("read")]
    public static object Read()
    {
      return Read(CurrentInputPort());
    }

    //[ThreadStatic]
    static Dictionary<object, Cons> readcache = new Dictionary<object, Cons>();

    [Builtin("read")]
    public static object Read(object port)
    {
      return ReadNext(port);
    }

    static object ReadNext(object port)
    {
      Cons c;
      if (readcache.TryGetValue(port, out c))
      {
        if (c.cdr == null)
        {
          readcache.Remove(port);
        }
        else
        {
          readcache[port] = c.cdr as Cons;
        }
        return c.car;
      }
      else
      {
        TextReader r = RequiresNotNull<TextReader>(port);

        try
        {
          object result = null;

          if (r is StreamReader)
          {
            StreamReader rr = (StreamReader)r;
            Stream s = rr.BaseStream;
            if (s == null)
            {
              return IOPortViolation("read", "port has already been closed", r);
            }
            if (s.Length == s.Position && !rr.EndOfStream)
            {
              s.Position = 0;
            }
            if (!rr.EndOfStream)
            {
              result = IronSchemeLanguageContext.ReadExpressions(s, Context.ModuleContext.CompilerContext);
              rr.ReadToEnd();
            }
          }
          else
          {
            string input = r.ReadToEnd();
            if (input.Length > 0)
            {
              result = IronSchemeLanguageContext.ReadExpressions(input, Context.ModuleContext.CompilerContext);
            }
          }

          if (result is Cons)
          {
            c = (Cons)result;
            if (c.cdr is Cons)
            {
              readcache[port] = c.cdr as Cons;
            }
            return c.car;
          }
          else if (result != null)
          {
            return result;
          }

          return EOF;
        }
        catch (IOException ex)
        {
          return IOPortViolation("read", ex.Message, port);
        }
        catch (Condition)
        {
          throw;
        }
        catch (Exception ex)
        {
          return AssertionViolation("read", ex.Message, port);
        }
      }
    }

    sealed class Eof 
    {
      public override string ToString()
      {
        return "#<eof>";
      }
    }

    protected readonly static object EOF = new Eof();

    [Builtin("eof-object")]
    public static object EofObject()
    {
      return EOF;
    }

    [Builtin("eof-object?")]
    public static object IsEof(object obj)
    {
      return GetBool(obj == EOF);
    }

    [Builtin("read-char")]
    public static object ReadChar()
    {
      return ReadChar(CurrentInputPort());
    }

    [Builtin("read-char")]
    public static object ReadChar(object port)
    {
      TextReader r = RequiresNotNull<TextReader>(port);
      try
      {
        int c = r.Read();
        if (c == -1)
        {
          return EOF;
        }
        return (char)c;
      }
      catch (IOException ex)
      {
        return IOPortViolation("read-char", ex.Message, port);
      }
      catch (Exception ex)
      {
        return AssertionViolation("read-char", ex.Message, port);
      }
    }

    [Builtin("peek-char")]
    public static object PeekChar()
    {
      return PeekChar(CurrentInputPort());
    }

    [Builtin("peek-char")]
    public static object PeekChar(object port)
    {
      TextReader r = RequiresNotNull<TextReader>(port);
      try
      {
        int c = r.Peek();
        if (c == -1)
        {
          return EOF;
        }
        return (char)c;
      }
      catch (IOException ex)
      {
        return IOPortViolation("peek-char", ex.Message, port);
      }
      catch (Exception ex)
      {
        return AssertionViolation("peek-char", ex.Message, port);
      }
    }

    [Builtin("write-char")]
    public static object WriteChar(object ch)
    {
      return WriteChar(ch, CurrentOutputPort());
    }

    [Builtin("write-char")]
    public static object WriteChar(object ch, object port)
    {
      try
      {
        TextWriter w = RequiresNotNull<TextWriter>(port);
        char c = RequiresNotNull<char>(ch);
        w.Write(c);
        return Unspecified;
      }
      catch (IOException ex)
      {
        return IOPortViolation("write-char", ex.Message, port);
      }
      catch (Exception ex)
      {
        return AssertionViolation("write-char", ex.Message, ch, port);
      }
    }


    [Builtin]
    public static object Newline()
    {
      return Newline(CurrentOutputPort());
    }

    [Builtin]
    public static object Newline(object port)
    {
      TextWriter w = RequiresNotNull<TextWriter>(port);
      try
      {
        w.Write('\n');
        return Unspecified;
      }
      catch (IOException ex)
      {
        return IOPortViolation("newline", ex.Message, port);
      }
      catch (Exception ex)
      {
        return AssertionViolation("newline", ex.Message, port);
      }
    }

    static readonly SymbolId quote = SymbolTable.StringToId("quote");
    static readonly SymbolId unquote_splicing = SymbolTable.StringToId("unquote-splicing");
    static readonly SymbolId quasiquote = SymbolTable.StringToId("quasiquote");
    static readonly SymbolId unquote = SymbolTable.StringToId("unquote");

    public static string DisplayFormat(object obj)
    {
      if (obj == null)
      {
        return "()";
      }
      if (obj is string)
      {
        return (string)obj;
      }
      if (obj is StringBuilder)
      {
        return obj.ToString();
      }

      if (obj is bool)
      {
        return ((bool)obj) ? "#t" : "#f";
      }
      if (obj is char)
      {
        return string.Format("{0}", obj);
      }

      if (obj is SymbolId)
      {
        return SymbolTable.IdToString((SymbolId)obj);
      }

      if ((bool)IsNumber(obj))
      {
        return NumberToString(obj) as string;
      }

      if (obj == EOF)
      {
        return "#<eof>";
      }
      if (obj == Builtins.Unspecified)
      {
        return "#<unspecified>";
      }
      if (obj is ICallable)
      {
        return "#<procedure " + obj + ">";
      }

      if (obj is Type)
      {
        Type t = (Type)obj;
        switch (Type.GetTypeCode(t))
        {
          case TypeCode.Boolean:
            return "Boolean";
          case TypeCode.Char:
            return "Character";
          case TypeCode.DateTime:
          case TypeCode.DBNull:
          case TypeCode.Empty:
          case TypeCode.Object:
            {
              if (t == typeof(SymbolId))
              {
                return "Symbol";
              }
              if (typeof(TextReader).IsAssignableFrom(t) || typeof(TextWriter).IsAssignableFrom(t))
              {
                return "Port";
              }
              if (t == typeof(Cons))
              {
                return "List";
              }
              if (t == typeof(Complex64) || t == typeof(Fraction))
              {
                return "Number";
              }
              if (typeof(ICallable).IsAssignableFrom(t))
              {
                return "Procedure";
              }
              if (t == typeof(object[]))
              {
                return "Vector";
              }
              return t.Name;
            }
          case TypeCode.String:
            return "String";
          default:
            return "Number";
        }

      }


      if (obj is Cons)
      {
        List<string> v = new List<string>();
        Cons s = obj as Cons;

        if (s != null)
        {
          object scar = s.car;
          if ((bool)IsSymbol(scar) && s.cdr is Cons && (int)Length(s.cdr) == 1)
          {
            if ((bool)IsEqual(quote, scar))
            {
              return "'" + DisplayFormat(Second(s));
            }
            if ((bool)IsEqual(quasiquote, scar))
            {
              return "`" + DisplayFormat(Second(s));
            }
            if ((bool)IsEqual(unquote, scar))
            {
              return "," + DisplayFormat(Second(s));
            }
            if ((bool)IsEqual(unquote_splicing, scar))
            {
              return ",@" + DisplayFormat(Second(s));
            }
          }
        }

        while (s != null)
        {
          v.Add(DisplayFormat(s.car));
          if (s.cdr != null && !(s.cdr is Cons))
          {
            v.Add(".");
            v.Add(DisplayFormat(s.cdr));
            break;
          }
          s = s.cdr as Cons;
        }
        return string.Format("({0})", string.Join(" ", v.ToArray()));
      }

      if (obj is byte[])
      {
        List<string> v = new List<string>();
        foreach (object io in (IList)obj)
        {
          v.Add(DisplayFormat(io));
        }

        return string.Format("#vu8({0})", string.Join(" ", v.ToArray()));
      }

      if (obj is object[])
      {
        List<string> v = new List<string>();
        foreach (object io in (IList)obj)
        {
          v.Add(DisplayFormat(io));
        }

        return string.Format("#({0})", string.Join(" ", v.ToArray()));
      }

      if (obj is Hashtable)
      {
        List<string> v = new List<string>();
        int llen = 0, rlen = 0;
        foreach (DictionaryEntry de in (Hashtable)obj)
        {
          llen = Math.Max(llen, DisplayFormat(de.Key).Length);
          rlen = Math.Max(rlen, DisplayFormat(de.Value).Length);
        }
        string format = string.Format("[{{0,-{0}}} => {{1,-{1}}}]", llen, rlen);

        foreach (DictionaryEntry de in (Hashtable)obj)
        {
          v.Add( string.Format(format,  DisplayFormat(de.Key), DisplayFormat(de.Value)));
        }

        if (v.Count == 0)
        {
          return "#[hashtable]";
        }

        return string.Join(Environment.NewLine, v.ToArray());
      }

      if (obj is Exception)
      {
        return cc.LanguageContext.Engine.FormatException(obj as Exception);
      }

      if (obj is R6RS.CompoundCondition)
      {
        R6RS.CompoundCondition cc = (R6RS.CompoundCondition)obj;

        List<string> ll = new List<string>();

        foreach (object o in cc.conds)
        {
          ll.Add(DisplayFormat(o));
        }

        return string.Format("<{0}>", string.Join(" ", ll.ToArray()));
      }

      if (obj is R6RS.Condition)
      {
        return obj.ToString();
      }

      if (obj is Exception)
      {
        return obj.GetType().Name.Replace("$", "&");
      }

      ICallable printer;
      if (R6RS.Records.printers.TryGetValue(obj.GetType().FullName, out printer))
      {
        StringWriter p = new StringWriter();
        printer.Call(obj, p);
        return p.ToString();
      }

      //finally check if this is some constructed type
      if (R6RS.Records.IsRecordAny(obj))
      {
        return R6RS.Records.PrintRecord(obj);
      }

      if (obj is MultipleValues)
      {
        object[] values = ((MultipleValues)obj).ToArray();
        return string.Join("\n", Array.ConvertAll<object, string>(values, DisplayFormat));
      }

      return obj.ToString();
    }

    [Builtin("display")]
    public static object Display(object obj)
    {
      return Display(obj, CurrentOutputPort());
    }

    [Builtin("display")]
    public static object Display(object obj, object port)
    {
      TextWriter w = RequiresNotNull<TextWriter>(port);
      string s = DisplayFormat(obj);
      w.Write(s);
      return Unspecified;
    }

    public static string WriteFormat(object obj)
    {
      if (obj == null)
      {
        return "()";
      }

      if (obj == EOF)
      {
        return "#<eof>";
      }
      if (obj == Builtins.Unspecified)
      {
        return "#<unspecified>";
      }
      if (obj is ICallable)
      {
        return "#<procedure " + obj + ">";
      }

      if (obj is bool)
      {
        return ((bool)obj) ? "#t" : "#f";
      }
      if (obj is string || obj is StringBuilder)
      {
        return string.Format("\"{0}\"", obj.ToString().
          Replace("\\", "\\\\").
          Replace("\"", "\\\"").
          Replace("\n", "\\n").
          Replace("\r", "\\r").
          Replace("\t", "\\t"));
      }
      if (obj is char)
      {
        switch ((char)obj)
        {
          case (char) 0: return "#\\nul";
          case (char) 7: return "#\\alarm";
          case (char) 8: return "#\\backspace";
          case (char) 9: return "#\\tab";
          case (char) 10: return "#\\newline";
          case (char) 11: return "#\\vtab";
          case (char) 12: return "#\\page";
          case (char) 13: return "#\\return";
          case (char) 27: return "#\\esc";
          case (char) 32: return "#\\space";
          case (char) 127: return "#\\delete";
        }

        return string.Format("#\\{0}", obj);
      }

      if (obj is Cons)
      {
        List<string> v = new List<string>();
        Cons s = obj as Cons;

        object scar = s.car;
        if ((bool)IsSymbol(scar) && s.cdr is Cons && (int)Length(s.cdr) == 1)
        {
          if ((bool)IsEqual(quote, scar))
          {
            return "'" + WriteFormat(Second(s));
          }
          if ((bool)IsEqual(quasiquote, scar))
          {
            return "`" + WriteFormat(Second(s));
          }
          if ((bool)IsEqual(unquote, scar))
          {
            return "," + WriteFormat(Second(s));
          }
          if ((bool)IsEqual(unquote_splicing, scar))
          {
            return ",@" + WriteFormat(Second(s));
          }
        }

        while (s != null)
        {
          v.Add(WriteFormat(s.car));
          if (s.cdr != null && !(s.cdr is Cons))
          {
            v.Add(".");
            v.Add(WriteFormat(s.cdr));
            break;
          }
          s = s.cdr as Cons;
        }
        return string.Format("({0})", string.Join(" ", v.ToArray()));
      }

      if (obj is byte[])
      {
        List<string> v = new List<string>();
        foreach (object io in (IList)obj)
        {
          v.Add(WriteFormat(io));
        }

        return string.Format("#vu8({0})", string.Join(" ", v.ToArray()));
      }

      if (obj is object[])
      {
        List<string> v = new List<string>();
        foreach (object io in (IList)obj)
        {
          v.Add(WriteFormat(io));
        }

        return string.Format("#({0})", string.Join(" ", v.ToArray()));
      }

      if (obj is SymbolId)
      {
        return SymbolTable.IdToString((SymbolId)obj);
      }

      if ((bool)IsNumber(obj))
      {
        return NumberToString(obj) as string;
      }

      ICallable printer;
      if (R6RS.Records.printers.TryGetValue(obj.GetType().FullName, out printer))
      {
        StringWriter p = new StringWriter();
        printer.Call(obj, p);
        return p.ToString();
      }

      //finally check if this is some constructed type
      if (R6RS.Records.IsRecordAny(obj))
      {
        return R6RS.Records.PrintRecord(obj);
      }

      if (obj is MultipleValues)
      {
        object[] values = ((MultipleValues)obj).ToArray();
        return string.Join("\n", Array.ConvertAll<object, string>(values, WriteFormat));
      }

      return obj.ToString();
    }

    [Builtin("write")]
    public static object Write(object obj)
    {
      return Write(obj, CurrentOutputPort());
    }

    [Builtin("write")]
    public static object Write(object obj, object port)
    {
      TextWriter w = RequiresNotNull<TextWriter>(port);
      string s = WriteFormat(obj);
      try
      {
        w.Write(s);
      }
      catch (Exception ex)
      {
        AssertionViolation("write", ex.Message, obj, port);
      }
      return Unspecified;
    }


    [Builtin("input-port?")]
    public static object IsInputPort(object obj)
    {
      return IsTrue(obj is TextReader); 
    }

    [Builtin("output-port?")]
    public static object IsOutputPort(object obj)
    {
      return IsTrue(obj is TextWriter);
    }

    [Builtin("call-with-input-file")]
    public static object CallWithInputFile(object filename, object fc1)
    {
      ICallable f = RequiresNotNull<ICallable>(fc1);
      string fn = RequiresNotNull<string>(filename);

      try
      {
        using (TextReader r = File.OpenText(GetPath(fn)))
        {
          object result = f.Call(r);

          if (readcache.ContainsKey(r))
          {
            readcache.Remove(r);
          }

          return result;
        }
      }
      catch (FileNotFoundException ex)
      {
        return FileNotFoundViolation("call-with-input-file", ex.Message, filename);
      }
      catch (Exception ex)
      {
        return AssertionViolation("call-with-input-filer", ex.Message, filename);
      }

    }

    [Builtin("call-with-output-file")]
    public static object CallWithOutputFile(object filename, object fc1)
    {
      ICallable f = RequiresNotNull<ICallable>(fc1);
      string fn = RequiresNotNull<string>(filename);

      try
      {
        using (TextWriter w = File.CreateText(GetPath(fn)))
        {
          return f.Call(w);
        }
      }
      catch (FileNotFoundException ex)
      {
        return FileNotFoundViolation("call-with-output-file", ex.Message, filename);
      }
      catch (Exception ex)
      {
        return AssertionViolation("call-with-output-file", ex.Message, filename);
      }
    }

    //probably a good idea to make these threadstatic
    [ThreadStatic]
    static TextReader currentinputport = Console.In;
    [ThreadStatic]
    static TextWriter currentoutputport = Console.Out;
    [ThreadStatic]
    static TextWriter currenterrorport = Console.Error;

    [Builtin("current-input-port")]
    public static object CurrentInputPort(object newport)
    {
      currentinputport = newport as TextReader;
      return Unspecified;
    }

    [Builtin("current-output-port")]
    public static object CurrentOutputPort(object newport)
    {
      currentoutputport = newport as TextWriter;
      return Unspecified;
    }


    [Builtin("current-input-port")]
    public static object CurrentInputPort()
    {
      return currentinputport;
    }

    [Builtin("current-output-port")]
    public static object CurrentOutputPort()
    {
      return currentoutputport;
    }

    [Builtin("current-error-port")]
    public static object CurrentErrorPort(object newport)
    {
      currenterrorport = newport as TextWriter;
      return Unspecified;
    }


    [Builtin("current-error-port")]
    public static object CurrentErrorPort()
    {
      return currenterrorport;
    }



    static string GetPath(string filename)
    {
      if (filename.StartsWith("~"))
      {
        if (ApplicationDirectory != Environment.CurrentDirectory)
        {
          return Path.Combine(ApplicationDirectory, filename.Substring(2));
        }
        else
        {
          return filename.Substring(2);
        }
      }
      return filename;
    }
    
    [Builtin("open-input-file")]
    public static object OpenInputFile(object filename)
    {
      string fn = RequiresNotNull<string>(filename);
      try
      {
        return File.OpenText(GetPath(fn));
      }
      catch (FileNotFoundException ex)
      {
        return FileNotFoundViolation("open-input-file", ex.Message, filename);
      }
      catch (Exception ex)
      {
        return AssertionViolation("open-input-file", ex.Message, filename);
      }      
    }

    [Builtin("open-output-file")]
    public static object OpenOutputFile(object filename)
    {
      string fn = RequiresNotNull<string>(filename);
      try
      {
        return File.CreateText(GetPath(fn));
      }
      catch (FileNotFoundException ex)
      {
        return FileNotFoundViolation("open-output-file", ex.Message, filename);
      }
      catch (Exception ex)
      {
        return AssertionViolation("open-output-file", ex.Message, filename);
      }
    }

    [Builtin("close-input-port")]
    public static object CloseInputPort(object port)
    {
      if (readcache.ContainsKey(port))
      {
        readcache.Remove(port);
      }
      RequiresNotNull<TextReader>(port).Close();
      return Unspecified;
    }

    [Builtin("close-output-port")]
    public static object CloseOutputPort(object port)
    {
      RequiresNotNull<TextWriter>(port).Close();
      return Unspecified;
    }



  }
}
