---
trigger: $kafka
label: CREATE CONNECTION … ON KAFKA
description: Apache Kafka connection with broker servers and topic
---
CREATE CONNECTION «ConnName» AS KAFKA(
  BOOTSTRAP_SERVERS = '«localhost:9092»',
  TOPIC             = '«topic»',
  GROUP_ID          = '«group-id»'
);
