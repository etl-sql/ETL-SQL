# Studio Learner Walkthrough

**Task:** ST16b. **Status:** not yet run. No learner has been observed.

Automation shows that the beginner sequences work. It cannot show that a learner understood them.
This sheet is the observation that closes that gap for the report journey (ST16, ST17) and the
ETL exercise (ST21, ST22). Its result feeds the
[beginner acceptance assessment](studio-beginner-acceptance-assessment.md).

## Who and how

- **Learner:** someone who has used Power BI, Tableau, SSRS or SSIS and has not used Studio.
- **Observer:** watches and writes. Does not touch the keyboard or name a control.
- **Hosts:** once in the Portal as a `Publisher` who owns one folder, once in the desktop host.
  The same learner may do both; record which came first.
- **Help:** when the learner is stuck for two minutes, give the smallest hint that unblocks them
  and record it. A hint is a finding, not a failure.
- **Time:** about 30 minutes per host.

Setup is the automated journeys' own: a sample report and a MOCKDB connection for the report part,
and the practice script from [Portal](studio-portal-practice-evidence.md) or
[desktop](studio-desktop-practice-evidence.md) evidence for the ETL part.

## Part 1: report

Ask the learner to do each step, then ask the question. Write what they say, not what they meant.

| # | Ask the learner to | Then ask | Understood if they say, in their own words |
| --- | --- | --- | --- |
| 1 | Add a bar chart from the sample data and give it a category and a value. | "Where does this chart get its rows?" | It reads the table or query named in `SOURCE`. |
| 2 | Open the chart's helper and look at the SQL it shows. | "What do the `MAPPINGS` lines do?" | They tie a column to a part of the chart, such as the axis or the bar height. |
| 3 | Add a filter to the chart. | "What changed in the script?" | A condition was added to the chart's source. |
| 4 | Type a comment and one small query in the code pane. | "If you open the helper again, what happens to what you typed?" | Nothing: the helper leaves hand-written text alone. Then have them check. |
| 5 | Save, close and reopen the report. | "Is this the script you saved?" | Yes, and they can point at their own comment. |
| 6 | Run the report. | "Which part of the script produced what you see?" | They point at the visual and its source. |

## Part 2: ETL exercise

| # | Ask the learner to | Then ask | Understood if they say, in their own words |
| --- | --- | --- | --- |
| 7 | Run the practice pipeline as given. | "What happened, and how do you know?" | It failed, and the message says three users were expected. |
| 8 | Find the line that caused it. | "Why did the check fail?" | The filter leaves out user 3, so only two rows reach the check. |
| 9 | Repair it and run again. | "Why not just delete the check?" | The check is what proves the load is right. |
| 10 | Look at the end of the script. | "What are the `DROP TABLE` lines for?" | They remove the working tables so a rerun starts clean. |
| 11 | Portal only: change a line and press Run without saving. | "Why was it refused?" | Run uses the saved version. |

## Record

### Scheduled returning-author walkthrough

- **Participant:** Chuck, Studio's creator; expert in Power BI, Tableau, SSRS and SSIS.
- **Previous Studio use:** has used Studio, but has not reviewed it in over a version.
- **Host order:** desktop first, then Portal.
- **Observations:** pending. No steps or understanding answers have been observed yet.
- **Scope:** returning-author feedback. This participant does not meet the new-to-Studio
  learner criterion above, so this walkthrough alone cannot close ST16b.

Copy this block once per learner and host.

```text
Date:                Host: Portal | Desktop        First host for this learner: yes | no
Learner background:  Power BI | Tableau | SSRS | SSIS | other:
Build or commit:

Step  Done without help  Hint given (exact words)        Learner's answer (their words)   Understood
1     yes | no                                                                            yes | partly | no
2
3
4
5
6
7
8
9
10
11

Steps where the learner was confused, and what they expected instead:

Controls the learner looked for and did not find:

Minutes to finish:   Part 1:        Part 2:
```

## Reading the result

- **Accepted** for a host when every step is done and at least nine of its answers are "yes",
  with no "no" on steps 2, 8 or 9. Those three are the SQL the learner generated and repaired.
- A step that needed a hint is recorded against the control or wording that caused it. Three
  learners needing the same hint is a defect to file, not a training note.
- One learner is evidence that the sequence can be understood. It is not a usability study; say
  how many learners were observed wherever the result is quoted.
