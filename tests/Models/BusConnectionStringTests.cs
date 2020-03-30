using System;
using FluentAssertions;
using TRRabbitMQ.Core.Models;
using Xunit;

namespace TRRabbitMQ.Tests.Models
{
    public class BusConnectionStringTests
    {
        [Fact]
        public void GivenBusConnectionStringWhenInvalidShouldThrow()
        {
            Func<BusConnectionString> action = () => BusConnectionString.Create("aaaa");

            action.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void GivenBusConnectionStringWhenUriIsValidShouldCreate()
        {
            var connectionString = BusConnectionString.Create("amqp://guest:guest@localhost/");

            connectionString.Should().NotBeNull();
        }

        [Fact]
        public void GivenConnectionStringsWhenValuesAreSameShouldBeEquals()
        {
            var connectionString1 = BusConnectionString.Create("amqp://guest:guest@localhost/");
            var connectionString2 = BusConnectionString.Create("amqp://guest:guest@localhost/");

            connectionString1.Should().Be(connectionString2);
        }
    }
}