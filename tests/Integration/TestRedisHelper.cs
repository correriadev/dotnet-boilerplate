using System.Collections.Concurrent;
using NSubstitute;
using StackExchange.Redis;

namespace Integration;

public static class TestRedisHelper
{
    public static IConnectionMultiplexer CriarMockRedisLimiter()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.IsConnected.Returns(true);
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var zset = new ConcurrentDictionary<string, List<(double score, string member)>>();

        db.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var keys = (RedisKey[])callInfo[1];
                var values = (RedisValue[])callInfo[2];
                var key = (string)keys[0]!;
                var nowMs = (double)(long)values[0];
                var windowMs = (double)(long)values[1];
                var limit = (int)(long)values[2];
                var nonce = (string)values[3]!;
                var clearBefore = nowMs - windowMs;

                var list = zset.GetOrAdd(key, _ => new List<(double, string)>());
                lock (list)
                {
                    list.RemoveAll(x => x.score < clearBefore);
                    if (list.Count < limit)
                    {
                        list.Add((nowMs, $"{nowMs}:{nonce}"));
                        return Task.FromResult(RedisResult.Create(1));
                    }
                    return Task.FromResult(RedisResult.Create(0));
                }
            });

        return redis;
    }
}
