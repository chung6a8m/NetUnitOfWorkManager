using System;

namespace NetUnitOfWorkManager
{
    /// <summary>
    /// Represents invalid Unit of Work lifecycle or scope usage.
    /// </summary>
    public sealed class UnitOfWorkStateException : InvalidOperationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnitOfWorkStateException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the invalid Unit of Work state.</param>
        public UnitOfWorkStateException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnitOfWorkStateException"/> class with an inner exception.
        /// </summary>
        /// <param name="message">The message that describes the invalid Unit of Work state.</param>
        /// <param name="innerException">The exception that caused the current exception.</param>
        public UnitOfWorkStateException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
