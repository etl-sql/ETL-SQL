---
trigger: $active_directory
label: CREATE CONNECTION … ON ACTIVE_DIRECTORY
description: Active Directory or LDAP search connection with credentials or integrated security
---
CREATE CONNECTION «ConnName» AS ACTIVE_DIRECTORY(
  HOST      = '«ldap.corp.com»',
  BASE_DN   = '«DC=corp,DC=com»',
  AUTH_MODE = 'NEGOTIATE',
  USER      = '«user»',
  PASSWORD  = '«password»'
);
