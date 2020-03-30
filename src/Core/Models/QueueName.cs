using System;
using System.Collections.Generic;
using TRDomainDriven.Core;

namespace TRRabbitMQ.Core.Models
{
    public class QueueName : ValueObject
    {
        private QueueName(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static QueueName Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            return new QueueName(name.Trim());
        }

        protected override IEnumerable<object> GetAtomicValues()
        {
            yield return Value;
        }
    }
}