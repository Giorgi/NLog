// 
// Copyright (c) 2004-2017 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

namespace NLog
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using NLog.Common;
    using NLog.Internal;
    using NLog.Layouts;
    using NLog.MessageTemplates;
    using NLog.Time;

    /// <summary>
    /// Represents the logging event.
    /// </summary>
    public class LogEventInfo
    {
        /// <summary>
        /// Gets the date of the first log event created.
        /// </summary>
        public static readonly DateTime ZeroDate = DateTime.UtcNow;
        internal static readonly LogMessageFormatter StringFormatMessageFormatter = GetStringFormatMessageFormatter;
        internal static LogMessageFormatter DefaultMessageFormatter { get; set; } = LogMessageTemplateFormatter.DefaultAuto.MessageFormatter;

        private static int globalSequenceId;


        /// <summary>
        /// The formatted log message. 
        /// </summary>
        private string _formattedMessage;

        /// <summary>
        /// The log message including any parameter placeholders
        /// </summary>
        private string _message;

        private object[] _parameters;
        private IFormatProvider _formatProvider;
        private LogMessageFormatter _messageFormatter = DefaultMessageFormatter;
        private IDictionary<Layout, string> _layoutCache;
        private PropertiesDictionary _properties;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogEventInfo" /> class.
        /// </summary>
        public LogEventInfo()
        {
            TimeStamp = TimeSource.Current.Time;
            SequenceID = Interlocked.Increment(ref globalSequenceId);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogEventInfo" /> class.
        /// </summary>
        /// <param name="level">Log level.</param>
        /// <param name="loggerName">Logger name.</param>
        /// <param name="message">Log message including parameter placeholders.</param>
        public LogEventInfo(LogLevel level, string loggerName, [Localizable(false)] string message)
            : this(level, loggerName, null, message, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogEventInfo" /> class.
        /// </summary>
        /// <param name="level">Log level.</param>
        /// <param name="loggerName">Logger name.</param>
        /// <param name="message">Log message including parameter placeholders.</param>
        /// <param name="messageTemplateParameters">Log message including parameter placeholders.</param>
        public LogEventInfo(LogLevel level, string loggerName, [Localizable(false)] string message, IList<MessageTemplateParameter> messageTemplateParameters)
            : this(level, loggerName, null, message, null, null)
        {
            if (messageTemplateParameters != null && messageTemplateParameters.Count > 0)
            {
                _properties = new PropertiesDictionary(messageTemplateParameters);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogEventInfo" /> class.
        /// </summary>
        /// <param name="level">Log level.</param>
        /// <param name="loggerName">Logger name.</param>
        /// <param name="formatProvider">An IFormatProvider that supplies culture-specific formatting information.</param>
        /// <param name="message">Log message including parameter placeholders.</param>
        /// <param name="parameters">Parameter array.</param>
        public LogEventInfo(LogLevel level, string loggerName, IFormatProvider formatProvider, [Localizable(false)] string message, object[] parameters) 
            : this(level, loggerName, formatProvider, message, parameters, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogEventInfo" /> class.
        /// </summary>
        /// <param name="level">Log level.</param>
        /// <param name="loggerName">Logger name.</param>
        /// <param name="formatProvider">An IFormatProvider that supplies culture-specific formatting information.</param>
        /// <param name="message">Log message including parameter placeholders.</param>
        /// <param name="parameters">Parameter array.</param>
        /// <param name="exception">Exception information.</param>
        public LogEventInfo(LogLevel level, string loggerName, IFormatProvider formatProvider, [Localizable(false)] string message, object[] parameters, Exception exception): this()
        {
            Level = level;
            LoggerName = loggerName;
            Message = message;
            Parameters = parameters;
            FormatProvider = formatProvider;
            Exception = exception;
         
            if (NeedToPreformatMessage(parameters))
            {
                CalcFormattedMessage();
            }
        }

        /// <summary>
        /// Gets the unique identifier of log event which is automatically generated
        /// and monotonously increasing.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "ID", Justification = "Backwards compatibility")]
        // ReSharper disable once InconsistentNaming
        public int SequenceID { get; private set; }

        /// <summary>
        /// Gets or sets the timestamp of the logging event.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1702:CompoundWordsShouldBeCasedCorrectly", MessageId = "TimeStamp", Justification = "Backwards compatibility.")]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// Gets or sets the level of the logging event.
        /// </summary>
        public LogLevel Level { get; set; }

        internal CallSiteInformation CallSiteInformation { get; private set; }
        internal CallSiteInformation GetCallSiteInformationInternal() { return CallSiteInformation ?? (CallSiteInformation = new CallSiteInformation()); }

        /// <summary>
        /// Gets a value indicating whether stack trace has been set for this event.
        /// </summary>
        public bool HasStackTrace => CallSiteInformation?.StackTrace != null;

        /// <summary>
        /// Gets the stack frame of the method that did the logging.
        /// </summary>
        public StackFrame UserStackFrame => CallSiteInformation?.UserStackFrame;

        /// <summary>
        /// Gets the number index of the stack frame that represents the user
        /// code (not the NLog code).
        /// </summary>
        public int UserStackFrameNumber => CallSiteInformation?.UserStackFrameNumberLegacy ?? CallSiteInformation?.UserStackFrameNumber ?? 0;

        /// <summary>
        /// Gets the entire stack trace.
        /// </summary>
        public StackTrace StackTrace => CallSiteInformation?.StackTrace;

        /// <summary>
        /// Gets the callsite member function name
        /// </summary>
        public string CallerMemberName => CallSiteInformation?.GetCallerMemberName(null, false, true, true);

        /// <summary>
        /// Gets the callsite source file path
        /// </summary>
        public string CallerFilePath => CallSiteInformation?.CallerFilePath;

        /// <summary>
        /// Gets the callsite source file line number
        /// </summary>
        public int CallerLineNumber => CallSiteInformation?.CallerLineNumber ?? 0;

        /// <summary>
        /// Gets or sets the exception information.
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Gets or sets the logger name.
        /// </summary>
        public string LoggerName { get; set; }

        /// <summary>
        /// Gets the logger short name.
        /// </summary>
        /// <remarks>This property was marked as obsolete on NLog 2.0 and it may be removed in a future release.</remarks>
        [Obsolete("This property should not be used. Marked obsolete on NLog 2.0")]
        public string LoggerShortName
        {
            // NOTE: This property is not referenced by NLog code anymore. 
            get
            {
                int lastDot = LoggerName.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    return LoggerName.Substring(lastDot + 1);
                }

                return LoggerName;
            }
        }

        /// <summary>
        /// Gets or sets the log message including any parameter placeholders.
        /// </summary>
        public string Message
        {
            get => _message;
            set
            {
                bool rebuildMessageTemplateParameters = ResetMessageTemplateParameters();
                _message = value;
                ResetFormattedMessage(rebuildMessageTemplateParameters);
            }
        }

        /// <summary>
        /// Gets or sets the parameter values or null if no parameters have been specified.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "For backwards compatibility.")]
        public object[] Parameters
        {
            get => _parameters;
            set
            {
                bool rebuildMessageTemplateParameters = ResetMessageTemplateParameters();
                _parameters = value;
                ResetFormattedMessage(rebuildMessageTemplateParameters);
            }
        }

        /// <summary>
        /// Gets or sets the format provider that was provided while logging or <see langword="null" />
        /// when no formatProvider was specified.
        /// </summary>
        public IFormatProvider FormatProvider
        {
            get => _formatProvider;
            set
            {
                if (_formatProvider != value)
                {
                    _formatProvider = value;
                    ResetFormattedMessage(false);
                }
            }
        }

        /// <summary>
        /// Gets or sets the message formatter for generating <see cref="LogEventInfo.FormattedMessage"/>
        /// Uses string.Format(...) when nothing else has been configured.
        /// </summary>
        public LogMessageFormatter MessageFormatter
        {
            get => _messageFormatter;
            set
            {
                _messageFormatter = value ?? StringFormatMessageFormatter;
                ResetFormattedMessage(false);
            }
        }

        /// <summary>
        /// Gets the formatted message.
        /// </summary>
        public string FormattedMessage
        {
            get 
            {
                if (_formattedMessage == null)
                {
                    CalcFormattedMessage();
                }

                return _formattedMessage;
            }
        }

        /// <summary>
        /// Checks if any per-event context properties (Without allocation)
        /// </summary>
        public bool HasProperties
        {
            get
            {
                if (_properties != null)
                {
                    return _properties.Count > 0;
                }
                else
                {
                    return HasMessageTemplateParameters;
                }
            }
        }

        internal PropertiesDictionary PropertiesDictionary { get => _properties; set => _properties = value; }

        /// <summary>
        /// Gets the dictionary of per-event context properties.
        /// </summary>
        public IDictionary<object, object> Properties => GetPropertiesInternal();

        /// <summary>
        /// Gets the dictionary of per-event context properties. 
        /// Internal helper for the PropertiesDictionary type.
        /// </summary>
        /// <returns></returns>
        private PropertiesDictionary GetPropertiesInternal()
        {
            if (_properties == null)
            {
                Interlocked.CompareExchange(ref _properties, new PropertiesDictionary(), null);
                if (HasMessageTemplateParameters)
                {
                    CalcFormattedMessage();
                    // MessageTemplateParameters have probably been created
                }
            }
            return _properties;
        }

        internal bool HasMessageTemplateParameters
        {
            get
            {
                var logMessageFormatter = _messageFormatter?.Target as ILogMessageFormatter;
                return logMessageFormatter?.HasProperties(this) ?? false;
            }
        }

        /// <summary>
        /// Gets the named parameters extracted from parsing <see cref="Message"/> as MessageTemplate
        /// </summary>
        public IMessageTemplateParameters MessageTemplateParameters
        {
            get
            {
                if (_properties != null && _properties.MessageProperties.Count > 0)
                {
                    return new MessageTemplateParameters(_properties.MessageProperties);
                }
                else
                {
                    return new MessageTemplateParameters(_message, _parameters);
                }
            }
        }

        /// <summary>
        /// Gets the dictionary of per-event context properties.
        /// </summary>
        /// <remarks>This property was marked as obsolete on NLog 2.0 and it may be removed in a future release.</remarks>
        [Obsolete("Use LogEventInfo.Properties instead.  Marked obsolete on NLog 2.0", true)]
        public IDictionary Context => GetPropertiesInternal().EventContext;

        /// <summary>
        /// Creates the null event.
        /// </summary>
        /// <returns>Null log event.</returns>
        public static LogEventInfo CreateNullEvent()
        {
            return new LogEventInfo(LogLevel.Off, String.Empty, String.Empty);
        }

        /// <summary>
        /// Creates the log event.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="loggerName">Name of the logger.</param>
        /// <param name="message">The message.</param>
        /// <returns>Instance of <see cref="LogEventInfo"/>.</returns>
        public static LogEventInfo Create(LogLevel logLevel, string loggerName, [Localizable(false)] string message)
        {
            return new LogEventInfo(logLevel, loggerName, null, message, null);
        }

        /// <summary>
        /// Creates the log event.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="loggerName">Name of the logger.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <param name="message">The message.</param>
        /// <param name="parameters">The parameters.</param>
        /// <returns>Instance of <see cref="LogEventInfo"/>.</returns>
        public static LogEventInfo Create(LogLevel logLevel, string loggerName, IFormatProvider formatProvider, [Localizable(false)] string message, object[] parameters)
        {
            return new LogEventInfo(logLevel, loggerName, formatProvider, message, parameters);
        }

        /// <summary>
        /// Creates the log event.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="loggerName">Name of the logger.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <param name="message">The message.</param>
        /// <returns>Instance of <see cref="LogEventInfo"/>.</returns>
        public static LogEventInfo Create(LogLevel logLevel, string loggerName, IFormatProvider formatProvider, object message)
        {
            return new LogEventInfo(logLevel, loggerName, formatProvider, "{0}", new[] { message });
        }

        /// <summary>
        /// Creates the log event.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="loggerName">Name of the logger.</param>
        /// <param name="message">The message.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Instance of <see cref="LogEventInfo"/>.</returns>
        /// <remarks>This method was marked as obsolete before NLog 4.3.11 and it may be removed in a future release.</remarks>
        [Obsolete("use Create(LogLevel logLevel, string loggerName, Exception exception, IFormatProvider formatProvider, string message) instead. Marked obsolete before v4.3.11")]
        public static LogEventInfo Create(LogLevel logLevel, string loggerName, [Localizable(false)] string message, Exception exception)
        {
            return new LogEventInfo(logLevel, loggerName, null, message, null, exception);
        }

        /// <summary>
        /// Creates the log event.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="loggerName">Name of the logger.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <param name="message">The message.</param>
        /// <returns>Instance of <see cref="LogEventInfo"/>.</returns>
        public static LogEventInfo Create(LogLevel logLevel, string loggerName, Exception exception, IFormatProvider formatProvider, [Localizable(false)] string message)
        {
            return Create(logLevel, loggerName, exception, formatProvider, message, null);
        }

        /// <summary>
        /// Creates the log event.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="loggerName">Name of the logger.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <param name="message">The message.</param>
        /// <param name="parameters">The parameters.</param>
        /// <returns>Instance of <see cref="LogEventInfo"/>.</returns>
        public static LogEventInfo Create(LogLevel logLevel, string loggerName, Exception exception, IFormatProvider formatProvider, [Localizable(false)] string message, object[] parameters)
        {
            return new LogEventInfo(logLevel, loggerName,formatProvider, message, parameters, exception);
        }

        /// <summary>
        /// Creates <see cref="AsyncLogEventInfo"/> from this <see cref="LogEventInfo"/> by attaching the specified asynchronous continuation.
        /// </summary>
        /// <param name="asyncContinuation">The asynchronous continuation.</param>
        /// <returns>Instance of <see cref="AsyncLogEventInfo"/> with attached continuation.</returns>
        public AsyncLogEventInfo WithContinuation(AsyncContinuation asyncContinuation)
        {
            return new AsyncLogEventInfo(this, asyncContinuation);
        }

        /// <summary>
        /// Returns a string representation of this log event.
        /// </summary>
        /// <returns>String representation of the log event.</returns>
        public override string ToString()
        {
            return $"Log Event: Logger='{LoggerName}' Level={Level} Message='{FormattedMessage}' SequenceID={SequenceID}";
        }

        /// <summary>
        /// Sets the stack trace for the event info.
        /// </summary>
        /// <param name="stackTrace">The stack trace.</param>
        /// <param name="userStackFrame">Index of the first user stack frame within the stack trace.</param>
        public void SetStackTrace(StackTrace stackTrace, int userStackFrame)
        {
            GetCallSiteInformationInternal().SetStackTrace(stackTrace, userStackFrame, null);
        }

        /// <summary>
        /// Sets the details retrieved from the Caller Information Attributes
        /// </summary>
        /// <param name="callerMemberName"></param>
        /// <param name="callerFilePath"></param>
        /// <param name="callerLineNumber"></param>
        public void SetCallerInfo(string callerMemberName, string callerFilePath, int callerLineNumber)
        {
            GetCallSiteInformationInternal().SetCallerInfo(callerMemberName, callerFilePath, callerLineNumber);
        }

        internal string AddCachedLayoutValue(Layout layout, string value)
        {
            if (_layoutCache == null)
            {
                Interlocked.CompareExchange(ref _layoutCache, new Dictionary<Layout, string>(), null);
            }
            lock (_layoutCache)
            {
                _layoutCache[layout] = value;
            }

            return value;
        }

        internal bool TryGetCachedLayoutValue(Layout layout, out string value)
        {
            if (_layoutCache == null)
            {
                // We don't need lock to see if dictionary has been created
                value = null;
                return false;
            }

            lock (_layoutCache)
            {
                if (_layoutCache.Count == 0)
                {
                    value = null;
                    return false;
                }

                return _layoutCache.TryGetValue(layout, out value);
            }
        }

        private static bool NeedToPreformatMessage(object[] parameters)
        {
            // we need to preformat message if it contains any parameters which could possibly
            // do logging in their ToString()
            if (parameters == null || parameters.Length == 0)
            {
                return false;
            }

            if (parameters.Length > 3)
            {
                // too many parameters, too costly to check
                return true;
            }

            if (!IsSafeToDeferFormatting(parameters[0]))
            {
                return true;
            }

            if (parameters.Length >= 2 && !IsSafeToDeferFormatting(parameters[1]))
            {
                return true;
            }

            if (parameters.Length >= 3 && !IsSafeToDeferFormatting(parameters[2]))
            {
                return true;
            }

            return false;
        }

        private static bool IsSafeToDeferFormatting(object value)
        {
            if (value == null)
            {
                return true;
            }

            return value.GetType().IsPrimitive() || (value is string);
        }

        private static string GetStringFormatMessageFormatter(LogEventInfo logEvent)
        {
            if (logEvent.Parameters == null || logEvent.Parameters.Length == 0)
            {
                return logEvent.Message;
            }
            else
            {
                return String.Format(logEvent.FormatProvider ?? CultureInfo.CurrentCulture, logEvent.Message, logEvent.Parameters);
            }
        }

        private void CalcFormattedMessage()
        {
            try
            {
                _formattedMessage = _messageFormatter(this);
            }
            catch (Exception exception)
            {
                _formattedMessage = Message;
                InternalLogger.Warn(exception, "Error when formatting a message.");

                if (exception.MustBeRethrown())
                {
                    throw;
                }
            }
        }

        private void ResetFormattedMessage(bool rebuildMessageTemplateParameters)
        {
            _formattedMessage = null;
            if (rebuildMessageTemplateParameters && HasMessageTemplateParameters)
            {
                CalcFormattedMessage();
            }
        }

        private bool ResetMessageTemplateParameters()
        {
            if (_properties != null && HasMessageTemplateParameters)
            {
                _properties.MessageProperties = null;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Set the <see cref="DefaultMessageFormatter"/>
        /// </summary>
        /// <param name="mode">true = Always, false = Never, null = Auto Detect</param>
        internal static void SetDefaultMessageFormatter(bool? mode)
        {
            if (mode == true)
            {
                InternalLogger.Info("Message Template Format always enabled");
                DefaultMessageFormatter = LogMessageTemplateFormatter.Default.MessageFormatter;
            }
            else if (mode == false)
            {
                InternalLogger.Info("Message Template String Format always enabled");
                DefaultMessageFormatter = StringFormatMessageFormatter;
            }
            else
            {
                //null = auto
                InternalLogger.Info("Message Template Auto Format enabled");
                DefaultMessageFormatter = LogMessageTemplateFormatter.DefaultAuto.MessageFormatter;
            }
        }
    }
}
