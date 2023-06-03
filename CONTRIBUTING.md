# Contributing

This doc describes getting set up to work with [git](https://git-scm.com/) to contribute to this project.

<!-- START doctoc generated TOC please keep comment here to allow auto update -->
<!-- DON'T EDIT THIS SECTION, INSTEAD RE-RUN doctoc TO UPDATE -->
**Table of Contents**  *generated with [DocToc](https://github.com/thlorenz/doctoc)*

- [Install Pre-Commit](#install-pre-commit)
- [Check for keys](#check-for-keys)

<!-- END doctoc generated TOC please keep comment here to allow auto update -->

## Install Pre-Commit

- Make sure you have an SSH key and optionally a GPG key [added to your GHE profile](https://docs.github.com/en/github/authenticating-to-github/managing-commit-signature-verification/adding-a-new-gpg-key-to-your-github-account).
- Optionally, configure git to use your GPG key for commit signing.

Set up your local repository:

1. clone this repo to your system

    ```sh
    git clone git@github.com:keegoid-nr/cloudscript.git
    ```

1. [install pre-commit](https://pre-commit.com/#install)

    ```sh
    brew install pre-commit
    pre-commit -V
    ```

1. From inside the repo, update repos defined in `.pre-commit-config.yaml` to latest tags and install the pre-commit git hook:

    ```sh
    cd cloudscript
    pre-commit autoupdate
    pre-commit install -t pre-commit -t pre-push -t prepare-commit-msg
    ```

    If you encounter any trouble with pre-commit, it is easy to uninstall.

    ```sh
    pre-commit uninstall -t pre-commit -t pre-push -t prepare-commit-msg
    ```

## Check for keys

Before committing changes, check for any customer api, license, or private location keys that may have made their way into your changes from logs or other info the customer sent. If you find any, replace them with [REDACTED].
