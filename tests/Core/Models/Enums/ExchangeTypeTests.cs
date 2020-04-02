using FluentAssertions;
using TRRabbitMQ.Core.Models.Enums;
using Xunit;

namespace TRRabbitMQ.Core.Tests.Models.Enums
{
    public class ExchangeTypeTests
    {
        [Fact]
        public void GivenExchangeTypesWhenSameValuesShouldBeEquals()
        {
            var type1 = ExchangeType.Topic;
            var type2 = ExchangeType.Topic;

            type1.Should().Be(type2);
        }

        [Fact]
        public void GivenExchangeTypeWhenDirectShouldSetValue()
        {
            var type = ExchangeType.Direct;

            type.Should().NotBeNull();
            type.Value.Should().Be("direct");
        }

        [Fact]
        public void GivenExchangeTypeWhenFanoutShouldSetValue()
        {
            var type = ExchangeType.Fanout;

            type.Should().NotBeNull();
            type.Value.Should().Be("fanout");
        }

        [Fact]
        public void GivenExchangeTypeWhenHeadersShouldSetValue()
        {
            var type = ExchangeType.Headers;

            type.Should().NotBeNull();
            type.Value.Should().Be("headers");
        }

        [Fact]
        public void GivenExchangeTypeWhenTopicShouldSetValue()
        {
            var type = ExchangeType.Topic;

            type.Should().NotBeNull();
            type.Value.Should().Be("topic");
        }
    }
}