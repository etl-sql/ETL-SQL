---
trigger: $api
label: CREATE CONNECTION … ON API
description: REST API connection with configurable method and authentication
---
CREATE CONNECTION «ConnName» ON API(
  URL       = '«https://api.example.com/v1/resource»',
  METHOD    = GET,
  AUTH_TYPE = BEARER,
  TOKEN     = '«your-token»'
);
