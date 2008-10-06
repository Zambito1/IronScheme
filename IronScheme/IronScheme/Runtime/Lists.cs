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
using IronScheme.Compiler;
using Microsoft.Scripting.Ast;

namespace IronScheme.Runtime
{
  public static partial class BuiltinEmitters
  {
    [InlineEmitter("null?")]
    public static Expression IsNull(Expression[] values)
    {
      if (values.Length != 1)
      {
        return null;
      }
      return Ast.Equal(values[0], Ast.Null());
    }
    
    [InlineEmitter("pair?")]
    public static Expression IsPair(Expression[] values)
    {
      if (values.Length != 1)
      {
        return null;
      }
      return Ast.TypeIs(values[0], typeof(Cons));
    }

    static FieldInfo car = typeof(Cons).GetField("car");
    static FieldInfo cdr = typeof(Cons).GetField("cdr");

    [InlineEmitter("car", Optimization=OptimizationLevel.Safe)]
    public static Expression Car(Expression[] values)
    {
      return ConsAccessor(values, car);
    }

    [InlineEmitter("cdr", Optimization = OptimizationLevel.Safe)]
    public static Expression Cdr(Expression[] values)
    {
      return ConsAccessor(values, cdr);
    }

    static Expression ConsAccessor(Expression[] values, FieldInfo field)
    {
      if (values.Length != 1)
      {
        return null;
      }
      Expression e = values[0];
      ConstantExpression c = e as ConstantExpression;
      if (c != null && c.Value == null)
      {
        return null;
      }

      if (e.Type != typeof(object) && e.Type != typeof(Cons))
      {
        return null;
      }
      return Ast.ReadField(Ast.ConvertHelper(e, typeof(Cons)), field);
    }

    static ConstructorInfo cons = typeof(Cons).GetConstructor(new Type[] { typeof(object), typeof(object) });

    [InlineEmitter("cons")]
    public static Expression Cons(Expression[] values)
    {
      if (values.Length == 2)
      {
        return Ast.New(cons, values[0], values[1]);  
      }
      return null;
    }


  }

  public partial class Builtins
  {
    [Builtin("pair?")]
    public static object IsPair(object arg1)
    {
      return GetBool(arg1 is Cons);
    }

    [Builtin("list?")]
    public static object IsList(object arg1)
    {
      if (arg1 == null)
      {
        return TRUE;
      }
      Cons c = arg1 as Cons;
      if (c == null)
      {
        return FALSE;
      }
      else
      {
        return GetBool(c.IsProper);
      }
    }

    [Builtin("null?")]
    public static object IsNull(object arg1)
    {
      return GetBool(arg1 == null);
    }

    [Builtin]
    public static Cons List(object arg1)
    {
      return new Cons(arg1);
    }

    [Builtin]
    public static Cons List(object arg1, object arg2)
    {
      return new Cons(arg1, new Cons(arg2));
    }

    [Builtin]
    public static Cons List(object arg1, object arg2, object arg3)
    {
      return new Cons(arg1, new Cons(arg2, new Cons(arg3)));
    }

    [Builtin]
    public static Cons List(object arg1, object arg2, object arg3, object arg4)
    {
      return new Cons(arg1, new Cons(arg2, new Cons(arg3, new Cons(arg4))));
    }

    [Builtin]
    public static Cons List(object arg1, object arg2, object arg3, object arg4, object arg5)
    {
      return new Cons(arg1, new Cons(arg2, new Cons(arg3, new Cons(arg4, new Cons(arg5)))));
    }


    [Builtin]
    public static Cons List(params object[] args)
    {
      return Runtime.Cons.FromArray(args);
    }

    // overload, no export, bad me!
    public static Cons Cons(object car)
    {
      return new Cons(car);
    }

    [Builtin]
    public static Cons Cons(object car, object cdr)
    {
      return new Cons(car, cdr);
    }

    [Builtin]
    public static object Length(object args)
    {
      Cons c = Requires<Runtime.Cons>(args);
      int length = 0;

      while (c != null)
      {
        length++;
        c = c.cdr as Cons;
      }

      return length;
    }
    
    [Builtin]
    public static object First(object args)
    {
      return Car(args);
    }

    [Builtin]
    public static object Second(object args)
    {
      return Car(Cdr(args));
    }

    [Builtin]
    public static object Third(object args)
    {
      return Car(Cdr(Cdr(args)));
    }
    
    [Builtin]
    public static object Last(object args)
    {
      Cons c = Requires<Runtime.Cons>(args);
      while (c.cdr is Cons)
      {
        c = c.cdr as Cons;
      }
      return c;
    }

    [Builtin("last-pair")]
    public static Cons LastPair(object args)
    {
      Cons c = Requires<Runtime.Cons>(args);
      while (c.cdr is Cons)
      {
        c = c.cdr as Cons;
      }
      return c;
    }

    [Builtin("make-list")]
    public static Cons MakeList(object n)
    {
      return VectorToList(MakeVector(n));
    }

    [Builtin("make-list")]
    public static Cons MakeList(object n, object fill)
    {
      return VectorToList(MakeVector(n, fill));
    }

    [Builtin("set-car!")]
    public static object SetCar(object list, object value)
    {
      Cons c = RequiresNotNull<Runtime.Cons>(list);
      c.car = value;
      return Unspecified;
    }

    [Builtin("set-cdr!")]
    public static object SetCdr(object list, object value)
    {
      Cons c = RequiresNotNull<Runtime.Cons>(list);
      c.cdr = value;
      return Unspecified;
    }

    [Builtin]
    public static object Rest(object args)
    {
      Cons c = RequiresNotNull<Runtime.Cons>(args);
      return c.cdr;
    }

    [Builtin]
    public static object Car(object args)
    {
      Cons c = RequiresNotNull<Runtime.Cons>(args);
      return c.car;
    }

    [Builtin]
    public static object Cdr(object args)
    {
      Cons c = RequiresNotNull<Runtime.Cons>(args);
      return c.cdr;
    }


    [Builtin]
    public static Cons Reverse(object lst)
    {
      Cons c = Requires<Runtime.Cons>(lst);
      Cons list = null;

      while (c != null)
      {
        list = new Cons(c.car, list);
        c = c.cdr as Cons;
      }

      return list;
    }


    [Builtin("list-tail")]
    public static object ListTail(object lst, object index)
    {
      Cons list = RequiresNotNull<Runtime.Cons>(lst);
      int i = RequiresNotNull<int>(index);
      object c = list;

      while (c != null)
      {
        if (i-- == 0)
        {
          return c;
        }

        c = ((Cons) c).cdr;
      }

      return AssertionViolation("list-tail", "index out of range", lst, index);
    }

    [Builtin("list-ref")]
    public static object ListRef(object lst, object index)
    {
      Cons list = RequiresNotNull<Runtime.Cons>(lst);
      int i = RequiresNotNull<int>(index);

      while (list != null)
      {
        if (i-- == 0)
        {
          return list.car;
        }
        list = list.cdr as Cons;
      }

      return AssertionViolation("list-ref", "index out of range", lst, index);
    }

    [Builtin]
    public static object Append()
    {
      return null;
    }

    [Builtin]
    public static object Append(object arg)
    {
      return arg;
    }

    [Builtin]
    public static object Append(object arg1, object arg2)
    {
      if (arg1 == null)
      {
        return arg2;
      }
      if (arg2 == null)
      {
        return arg1;
      }
      return Append(new object[] { arg1, arg2 });
    }

    //The resulting list is always newly allocated, except that it shares structure with the last list argument. 
    //The last argument may actually be any object; an improper list results if the last argument is not a proper list. 
    [Builtin]
    public static object Append(params object[] args)
    {
      if (args.Length == 0)
      {
        return null;
      }
      // some fast rules
      if (args.Length == 1)
      {
        return args[0];
      }

      if (args.Length == 2)
      {
        if (args[0] == null)
        {
          return args[1];
        }
        if (args[1] == null)
        {
          return args[0];
        }
      }

      Cons head = null, h = null;

      for (int i = 0; i < args.Length - 1; i++)
      {
        Cons ii = args[i] as Cons;
        if (null == args[i])
        {
          //empty list, do nothing
        }
        else if (ii != null)
        {
          while (ii != null)
          {
            Cons cc = new Cons(ii.car);
            if (head == null)
            {
              h = head = cc;
            }
            else
            {
              h.cdr = cc;
              h = cc;
            }

            ii = ii.cdr as Cons;
          }
        }
        else
        {
          AssertionViolation(SymbolTable.StringToId("append"), "not a list", args[i]);
        }
        
      }

      object tail = args[args.Length - 1];

      if (h == null)
      {
        return tail;
      }
      h.cdr = tail;
      return head;
    }

    [Builtin("reverse!")]
    public static Cons NReverse(Cons list)
    {
      if (list == null)
      {
        return null;
      }

      Cons prev = null, next = null;

      while (true)
      {
        next = list.cdr as Cons;
        list.cdr = prev;

        if (next == null)
        {
          return list;
        }

        prev = list;
        list = next;
      }
    }

    public static Cons ToImproper(Cons c)
    {
      Cons i = c;
      Cons j = null;

      while (i.cdr != null)
      {
        j = i;
        i = i.cdr as Cons;
        if (i == null)
        {
          return c; // improper already
        }
      }

      j.cdr = i.car;
      return c;
    }

    [Builtin]
    public static object Distinct(object lst)
    {
      Hashtable set = new Hashtable();
      Cons c = Requires<Runtime.Cons>(lst);

      while (c != null)
      {
        set[c.car] = true;
        c = c.cdr as Cons;
      }

      return Runtime.Cons.FromList(set.Keys);
    }

  }
}
