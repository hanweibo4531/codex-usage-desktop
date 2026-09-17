# Project completion workflow

- After each completed change, build and run the regression checks, replace the local copy the user is running, and restart the desktop application. Check the new process and executable version.
- Commit and push the completed changes to the existing GitHub repository. For a new version, publish a Release with the portable Windows ZIP and verify the uploaded asset; source-only upload is not sufficient.
- Package only the executable, public documentation/license and assets. Keep credentials, local settings, request history, session logs and diagnostic output out of Git and release archives.
- Scheduled-request tests use mocks or disabled Windows tasks. Do not enable a user's schedule or send an immediate real model request merely to test a change.
