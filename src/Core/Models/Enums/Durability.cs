using System.Collections.Generic;
using TRDomainDriven.Core;

namespace TRRabbitMQ.Core.Models.Enums
{
    public class Durability : ValueObject
    {
        private Durability(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public bool IsDurable => this == Durable;

        public static Durability Durable => new Durability(nameof(Durable));
        public static Durability Transient => new Durability(nameof(Transient));


        protected override IEnumerable<object> GetAtomicValues()
        {
            yield return Value;
        }
    }
}