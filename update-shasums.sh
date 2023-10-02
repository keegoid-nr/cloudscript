#!/bin/bash
sha512sum cs > SHA512SUMS
gpg --detach-sign --armor SHA512SUMS
gpg --keyid-format long --verify SHA512SUMS.asc SHA512SUMS
sha512sum -c SHA512SUMS
