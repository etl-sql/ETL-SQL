---
trigger: $mongodb
label: CREATE CONNECTION … ON MONGODB
description: MongoDB connection with host, database, and collection
---
CREATE CONNECTION «ConnName» AS MONGODB(
  URI        = '«mongodb://localhost:27017»',
  DATABASE   = '«database»',
  COLLECTION = '«collection»'
);
