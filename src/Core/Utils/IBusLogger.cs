using System;
using System.Collections.Generic;

namespace TRRabbitMQ.Core.Utils
{
    public interface IBusLogger
    {
        void WriteException(string name, Exception exception, params KeyValuePair<string, object>[] properties);
    }
}