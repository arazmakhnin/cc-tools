# cc-tools
Tools for automation usual CC processes: creating new ticket, making new PR, etc.

**Before run, please check "settings.json" and fill your credentials and settings.**

Commands:
- new: create new ticket
- pr: make new pr from changes in your working directory
- review: move ticket from "In progress" to "Ready for review"
- fp: set ticket to FP
- ci: trigger CI for specific PR
- solve: automatically solve ticket (only "BRP magic strings" are supported for now)
