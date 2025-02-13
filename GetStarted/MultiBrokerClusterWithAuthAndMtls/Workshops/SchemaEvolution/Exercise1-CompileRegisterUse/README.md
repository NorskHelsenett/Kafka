This is the first exercise in the schema evolution workshop.

Our focus here will be to create a schema, compile it, and then use it to produce some messages to Kafka.
As we won't have time to deep dive into everything, the code for registering the compiled schema with the
schema registry is provided, as well as some utility classes for creating the topic we'll be be producing to,
 as well as binding the config for reaching and authing with Kafka itself.

The starting point for this exercise should be going to the SchemaEvolution/Protos/ directory,
and creating your schema in the Waffle.proto file.
You can then follow the instruction in that folders readme to compile it.
Thereafter, you'll want to go to the SchemaEvolution/WaffleMaker.cs file, and use the generated dotnet
mapping class(es) to produce a couple of messages using your new schema to Kafka.
You should be able to use the provided `KafkaConfigBinder.GetProducerConfig()` to get a producer config for talking to Kafka,
and similarily `KafkaConfigBinder.GetSchemaRegistryConfig()` to obtain a config for the Kafka packages to reach the schema registry.

