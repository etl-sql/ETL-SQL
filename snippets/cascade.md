---
trigger: $cascade
label: CASCADE dependent slicer
description: Add a local-vector cascade policy to a slicer or multiselect visual
---
CASCADE (
  MODE = LOCAL,
  PARENTS (@«ParentParameter» = «ParentColumn»),
  INVALID = CLEAR,
  NULL = ALL,
  ALL_VALUE = '*',
  MULTISELECT = ANY
)
