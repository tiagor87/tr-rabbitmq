using System;
using System.Collections.Generic;
using TRDomainDriven.Core;

namespace TRRabbitMQ.Core.Models
{
    public class ExchangeName : ValueObject
    {
        private ExchangeName(string value)
        {
            Value = value;
        }

        public string Value { get; }
        public static ExchangeName Default => new ExchangeName(string.Empty);

        public static ExchangeName Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            return new ExchangeName(name.Trim());
        }

        protected override IEnumerable<object> GetAtomicValues()
        {
            yield return Value;
        }
    }
}