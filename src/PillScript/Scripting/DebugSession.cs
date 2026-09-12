using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace PillScript
{
    /// <summary>One local as it stood when the script stopped.</summary>
    public sealed class DebugVariable
    {
        public string Name { get; internal set; }
        public string Type { get; internal set; }
        public string Value { get; internal set; }
    }

    /// <summary>Raised when an instrumented script reaches a breakpoint.</summary>
    public sealed class DebugStop
    {
        public string File { get; internal set; }
        public int Line { get; internal set; }
        public IReadOnlyList<DebugVariable> Variables { get; internal set; }
    }

    /// <summary>Thrown to abandon a script that is sitting at a breakpoint.</summary>
    public sealed class DebugStoppedException : Exception
    {
        public DebugStoppedException() : base("The script was stopped at a breakpoint.") { }
    }

    /// <summary>
    /// The other half of a breakpoint. The compiler puts a call to <see cref="Break"/> in front of
    /// every line the editor has marked; this holds the solve there and shows the editor what the
    /// locals were, until somebody continues or stops.
    /// </summary>
    public static class DebugSession
    {
        static readonly object Gate = new object();
        static DispatcherFrame _frame;
        static bool _stopping;

        /// <summary>Raised on the thread the script runs on when a breakpoint is reached.</summary>
        internal static event Action<DebugStop> Paused;

        /// <summary>Raised once the script has been let go.</summary>
        internal static event Action Resumed;

        /// <summary>True while a script is being held at a breakpoint.</summary>
        internal static bool IsPaused { get; private set; }

        /// <summary>
        /// Called by instrumented scripts. The arguments alternate name and value, which is the
        /// shape that is cheapest for the rewriter to emit.
        /// </summary>
        public static void Break(string file, int line, object[] locals)
        {
            if (Paused == null) return;
            // Without a dispatcher there is no loop to pump, so there is no way to pause safely.
            if (Dispatcher.FromThread(Thread.CurrentThread) == null) return;

            var stop = new DebugStop
            {
                File = file,
                Line = line,
                Variables = Describe(locals)
            };

            lock (Gate)
            {
                if (IsPaused) return;
                IsPaused = true;
                _stopping = false;
            }

            try
            {
                Paused.Invoke(stop);

                // A nested message loop keeps Rhino answering while the solve stands still, the
                // same trick a modal dialog uses. Without it a breakpoint would freeze the host.
                _frame = new DispatcherFrame();
                Dispatcher.PushFrame(_frame);
            }
            finally
            {
                _frame = null;
                IsPaused = false;

                Resumed?.Invoke();
            }

            if (_stopping) throw new DebugStoppedException();
        }

        /// <summary>Lets a paused script carry on.</summary>
        internal static void Continue()
        {
            _stopping = false;
            Release();
        }

        /// <summary>Abandons the paused script, which ends the component's solve.</summary>
        internal static void Stop()
        {
            _stopping = true;
            Release();
        }

        static void Release()
        {
            var frame = _frame;
            if (frame != null) frame.Continue = false;
        }

        static List<DebugVariable> Describe(object[] locals)
        {
            var described = new List<DebugVariable>();
            if (locals == null) return described;

            for (var i = 0; i + 1 < locals.Length; i += 2)
            {
                var value = locals[i + 1];

                described.Add(new DebugVariable
                {
                    Name = locals[i] as string ?? "?",
                    Type = value == null ? "null" : Name(value.GetType()),
                    Value = Format(value)
                });
            }

            return described;
        }

        static string Name(Type type)
        {
            if (!type.IsGenericType) return type.Name;

            var arguments = string.Join(", ", type.GetGenericArguments().Select(Name));
            var name = type.Name;
            var tick = name.IndexOf('`');

            if (tick > 0) name = name.Substring(0, tick);

            return name + "<" + arguments + ">";
        }

        /// <summary>A short, readable rendering: collections say how many, everything else says what.</summary>
        static string Format(object value)
        {
            if (value == null) return "null";
            if (value is string text) return "\"" + Shorten(text) + "\"";

            if (value is IEnumerable items && !(value is string))
            {
                var list = items.Cast<object>().Take(6).ToList();
                var count = items.Cast<object>().Count();

                var head = string.Join(", ", list.Select(item => Shorten(item?.ToString() ?? "null")));
                if (count > list.Count) head += ", ...";

                return count + " items [" + head + "]";
            }

            return Shorten(value.ToString());
        }

        static string Shorten(string text)
        {
            text = (text ?? string.Empty).Replace(Environment.NewLine, " ");
            return text.Length <= 120 ? text : text.Substring(0, 117) + "...";
        }
    }
}
