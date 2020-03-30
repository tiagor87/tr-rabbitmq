using System;
using System.Collections.Generic;
using TRDomainDriven.Core;

namespace TRRabbitMQ.Core.Models
{
    public class BusConnectionString : ValueObject
    {
        private BusConnectionString(Uri value)
        {
            Value = value;
        }

        public Uri Value { get; }

        public static BusConnectionString Create(string connectionString)
        {
            if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("The connection string is invalid.", nameof(connectionString));
            }

            return new BusConnectionString(uri);
        }

        protected override IEnumerable<object> GetAtomicValues()
        {
            yield return Value;
        }
    }
}