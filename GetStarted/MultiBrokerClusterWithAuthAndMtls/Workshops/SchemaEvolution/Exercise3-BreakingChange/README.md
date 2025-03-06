This is the third exercise in the schema evolution workshop.

The focus here is mostly about reinforcing what's already been covered in the previous exercises,
by doing a slight variation of it one more time, but with a small twist.

The goal in this exercise is to make an incompatible schema, and use that to push some data to Kafka.
To achieve this, we need a new topic to push to. As the goal of this exercise is not to instrument Kafka,
the code for creating topics and registering schemas is still provided and pre configured.

As previously, the utility classes for connecting and authing with Kafka are provided. So you can still
use `KafkaConfigBinder.GetProducerConfig()` to get a producer config for talking to Kafka, and similarly
`KafkaConfigBinder.GetSchemaRegistryConfig()` to obtain a config for the Kafka packages to reach the
schema registry.

You'll probably want to start out by making a new schema. After compiling it, set up the prerequisites
in Program.cs as before. Once everything is in place, you can try producing to it.

If after the first couple of exercises this is starting to feel a little to simple, you are encouraged to
try out a couple of things that should not work, and seeing how they fail. For instance, try registering
your new not backwards compatible schema with a name indicating the previous exercises topic. Or try
using your new schema to produce to the old topic without registering the schema first. In the later
scenario, what happens when you tell the producer/serializer to autoregister the schema?
