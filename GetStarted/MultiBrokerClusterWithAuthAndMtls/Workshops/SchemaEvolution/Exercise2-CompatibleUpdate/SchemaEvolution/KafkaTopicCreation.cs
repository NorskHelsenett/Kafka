using Confluent.Kafka;
using Confluent.Kafka.Admin;

public static class KafkaTopicCreation
{
    public static async Task TryCreateTopic(string topic)
    {
        var adminClientConfig = KafkaConfigBinder.GetAdminClientConfig();

        using var adminClient = new AdminClientBuilder(adminClientConfig).Build();
        try
        {
            Console.WriteLine($"Asking admin client to create topic {topic} in case it doesn't exist");
            await adminClient.CreateTopicsAsync([
                new TopicSpecification
                    {
                         Name = topic,
                         ReplicationFactor = -1,
                         NumPartitions = -1,
                         Configs = new Dictionary<string, string>
                         {
                            { "cleanup.policy", "compact" },
                            { "retention.bytes", "-1" },
                            { "retention.ms", "-1" },
                            { "min.compaction.lag.ms", $"{TimeSpan.FromMinutes(15).TotalMilliseconds}" },
                            { "max.compaction.lag.ms", $"{TimeSpan.FromHours(1).TotalMilliseconds}" },
                            { "segment.ms", $"{TimeSpan.FromHours(2).TotalMilliseconds}" },
                            { "delete.retention.ms", $"{TimeSpan.FromDays(1).TotalMilliseconds}" },
                            { "min.cleanable.dirty.ratio", "0.90" },
                        }
                    }
            ]);
            Console.WriteLine($"Admin client done trying to create topic {topic}");
        }
        catch (Exception e)
        {
            if (e is CreateTopicsException && e.Message.Contains($"Topic '{topic}' already exists."))
            {
                // Doing it this way with exception to check is kind of bad, but for a 1-off "check this during startup" it's not really worth it to start the "query cluster for info about everything including all topics" dance.
                // Still, don't do this at home kids.
                Console.WriteLine($"Admin client did not create topic {topic} because it already exists");
                return;
            }
            Console.WriteLine(e);
            Console.WriteLine("An error occurred creating topic");
        }
    }
}
