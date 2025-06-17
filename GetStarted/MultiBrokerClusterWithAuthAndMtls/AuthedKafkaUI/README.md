This is a demo of how you can spin up [Kafka Kafbat UI](https://ui.docs.kafbat.io/) with auth, and play around with different restrictions for different users and so on.
The usual caveat emptors of not deploying this directly to production because you have to make adjustments for security and functionality in your environment etc. apply as always.

It is intended to be run with a kafka cluster and schema registry like the one found in the compose one directory above ([this one](../docker-compose.yaml)).
Basically after you've started that and it's stable and up and running, you can start this by running `docker compose up -d` in this directory.

Once you've started it you will have a [keycloak](https://www.keycloak.org/) instance at http://localhost:8088 and the authed Kafka UI at http://localhost:8082 (for comparison the regular unauthed KafkaUI should still be at http://localhost:8081).

To log in to the Kafka UI you can use these users:

| Username      | Password   |
|---------------|------------|
| `kui-admin`   | `password` |
| `kui-user`    | `password` |
| `kui-user-ro` | `password` |

For the Keycloak UI found at http://localhost:8088 you should be able to log in the the user `admin` user the password `password` should you want to adjust things there.
