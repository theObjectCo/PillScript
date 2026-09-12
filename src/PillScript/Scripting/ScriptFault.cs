using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PillScript.Scripting
{
    /// <summary>
    /// Says what went wrong in a script in the terms its author is working in: the exception that
    /// was actually thrown, and the line of their own code it came from.
    /// </summary>
    internal static class ScriptFault
    {
        /// <summary>
        /// Calling a script through reflection wraps whatever it threw. The wrapper says nothing
        /// the author can act on, so it is stepped past.
        /// </summary>
        public static Exception Unwrap(Exception exception)
            => exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;

        /// <summary>
        /// Names the exception and, when the script's own frames are in the trace, where it
        /// happened. The scripts are compiled with debug information, so usually they are.
        /// </summary>
        public static string Describe(Exception exception)
        {
            var message = exception.GetType().Name + ": " + exception.Message;

            var frame = new StackTrace(exception, true).GetFrames()?
                .FirstOrDefault(f => f.GetFileLineNumber() > 0);

            if (frame == null) return message;

            var file = Path.GetFileName(frame.GetFileName());
            return message + " (" + file + ", line " + frame.GetFileLineNumber() + ")";
        }
    }
}
