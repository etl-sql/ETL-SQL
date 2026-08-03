### Security

- Report alerts are now re-authorized against their owner before every evaluation, the same way subscription delivery already was. An alert kept firing after its author was deactivated or lost their last grant on the report, and an alert notification carries the value that crossed the threshold — so a departed author's alert went on pushing report data into the channel they had chosen, and disabling the account did not stop it. Unauthorized alerts are now skipped whole rather than evaluated with the dispatch suppressed, so a `TRIGGERED` transition is never recorded against a notification nobody received.

  Found by auditing revocation across connections, subscriptions, alerts, and saved views. The other three were already correct: subscriptions re-authorize at delivery, saved-view routes resolve report permission before narrowing to the caller's own rows, and shared connections have no authorship path at all.
