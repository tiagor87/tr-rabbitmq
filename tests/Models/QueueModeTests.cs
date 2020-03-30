using FluentAssertions;
using TRRabbitMQ.Core.Models.Enums;
using Xunit;

namespace TRRabbitMQ.Tests.Models
{
    public class QueueModeTests
    {
        [Fact]
        public void GivenQueueModesWhenSameValueShouldBeEquals()
        {
            var queueMode1 = QueueMode.Normal;
            var queueMode2 = QueueMode.Normal;

            queueMode1.Should().Be(queueMode2);
        }

        [Fact]
        public void GivenQueueModeWhenLazyShouldValueBeLazy()
        {
            var queueMode = QueueMode.Lazy;

            queueMode.Should().NotBeNull();
            queueMode.Value.Should().Be("lazy");
        }

        [Fact]
        public void GivenQueueModeWhenNormalShouldValueBeEmpty()
        {
            var queueMode = QueueMode.Normal;

            queueMode.Should().NotBeNull();
            queueMode.Value.Should().Be(string.Empty);
        }
    }
}