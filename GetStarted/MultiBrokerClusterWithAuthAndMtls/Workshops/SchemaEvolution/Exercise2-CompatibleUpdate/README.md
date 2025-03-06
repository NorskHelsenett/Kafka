This is the second exercise in the schema evolution workshop.

Our focus here will be to create a new backwards compatible schema, compile it, and use the new schema to
produce some messages to the same topic in Kafka which we used for the previous exercise.

Once again utility classes for connecting and authing with Kafka are provided. So you can still use
`KafkaConfigBinder.GetProducerConfig()` to get a producer config for talking to Kafka, and similarly
`KafkaConfigBinder.GetSchemaRegistryConfig()` to obtain a config for the Kafka packages to reach the
schema registry.

Your starting point is to based on the previous schema produce a backwards compatible one. Put the .proto
definition in the Protos folder, and compile it same as for the previous exercise. Once you have named it
and compiled it, update the reference to the proto file in Program.cs so that the new schema is shipped to
the schema registry. Then, all that remains is creating a producer and trowing some more data at Kafka!
If you get stuck at this point, you can look at the solution to the previous exercise in WaffleMaker.cs
for an example of how it could be done.
