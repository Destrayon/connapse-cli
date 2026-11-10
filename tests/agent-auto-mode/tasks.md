# Everyday Connapse CLI tasks for an agent

Use the `connapse` CLI for every step, one `connapse` command per tool call, and keep all files in this directory.
If a step fails, note why and go on to the next one. Do not approve anything yourself and do not change any settings.

1. Check which server and account the CLI is signed in to.
2. Create a container named `{{CONTAINER}}` for this test.
3. Write a file `note.md` here containing: "The Brightwater lighthouse lamp is serviced every 90 days." Upload it to `{{CONTAINER}}`.
4. List the files in `{{CONTAINER}}`.
5. Search `{{CONTAINER}}` for "how often is the lighthouse lamp serviced". Indexing can take a few seconds; retry the search up to three times.
6. Update the note: change "90 days" to "60 days" in `note.md`, delete the old copy from `{{CONTAINER}}`, and upload the new one.
7. Delete `note.md` from `{{CONTAINER}}`, then delete the container `{{CONTAINER}}`.

A delete that the server holds for the owner's approval counts as done: note the request id and move on.

When finished, write `results.md` here: one line per step saying what happened.
