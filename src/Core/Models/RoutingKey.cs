using System;
using System.Collections.Generic;
using TRDomainDriven.Core;

namespace TRRabbitMQ.Core.Models
{
    public class RoutingKey : ValueObject
    {
        private RoutingKey(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static RoutingKey Create(string routing)
        {
            if (string.IsNullOrWhiteSpace(routing))
            {
                throw new ArgumentNullException(nameof(routing));
            }

            return new RoutingKey(routing.Trim());
        }

        protected override IEnumerable<object> GetAtomicValues()
        {
            yield return Value;
        }
    }
}