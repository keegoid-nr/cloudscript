# CloudScript

<!-- START doctoc generated TOC please keep comment here to allow auto update -->
<!-- DON'T EDIT THIS SECTION, INSTEAD RE-RUN doctoc TO UPDATE -->
**Table of Contents**  *generated with [DocToc](https://github.com/thlorenz/doctoc)*

- [Requirements](#requirements)
- [Usage](#usage)
  - [EC2](#ec2)
  - [EKS](#eks)
  - [Lambda](#lambda)
- [Contributing](#contributing)

<!-- END doctoc generated TOC please keep comment here to allow auto update -->

A script for managing states of [EC2 instances](https://aws.amazon.com/ec2/) and [EKS clusters](https://aws.amazon.com/eks/) on AWS and getting info about published [New Relic Lambda layers](https://aws.amazon.com/eks/).

## Requirements

AWS CLI v2 must be installed and configured, like by running `aws configure`.  
https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2.html

jq must be installed.  
https://stedolan.github.io/jq/

eksctl must be installed for use with EKS.  
https://eksctl.io/

unzip, xargs, and curl must also be available.

## Usage

```log
Usage: cs COMPONENT <REQUIRED ARGS> [OPTIONAL ARGS]

About:
  cs -v, --version  Show version
  cs --help         Show this message

Components:
  ec2     Manage EC2 instance states
  eks     Manage EKS node states
  lambda  List and download New Relic Lambda layers

Components and Args:
  ec2 status
  ec2 start|stop|restart|ssh <instanceId>
  eks status
  eks start <cluster> <number of nodes>
  eks stop <cluster>
  lambda list-layers [runtime]|[all] [region]
  lambda download-layers [layer]|[all] [region] [build]|[latest] [extension]|[agent]

Examples:
  cs ec2 status
  cs eks start my-cluster 2
  cs lambda list-layers                                     List layer names
  cs lambda list-layers all                                 Details for all layers
  cs lambda list-layers nodejs18.x us-west-2                Details for a specific layer
  cs lambda download-layers NewRelicNodeJS18X us-west-2 24  Download build #24 for a layer
  cs lambda download-layers all us-west-2 latest extension  Download all latest layers & show extension details
```

1. Copy `cs` to your PATH.
1. Verify it is in your path.

    ```sh
    which cs
    cs --version
    ```

1. Run a command (see below examples).

### EC2

get a list of instances and their IDs

```sh
cs ec2 start
```

start an instance

```sh
cs ec2 start <your-instance-id>
```

stop an instance

```sh
cs ec2 stop <your-instance-id>
```

### EKS

get a list of cluster names

```sh
cs eks start
```

scale up an EKS node group to 2 nodes

```sh
cs eks start <your-cluster-name> 2
```

stop an eks node group (scale down to 0 nodes)

```sh
cs eks stop <your-cluster-name>
```

- If leaving off the optional instanceId a list of instanceIds will be shown, but only those with a `Name` tag.

  ```json
            "Tags": [
              {
                "Key": "Name",
                "Value": "your-ec2-instance-name"
              }
            ],
  ```

### Lambda

get a list of compatible runtimes

```sh
cs lambda list-layers
```

get a list of layer names

```sh
cs lambda download-layers
```

download all layers and stat extension release dates

```sh
cs lambda download-layers all us-west-2 latest extension
```

download all layers and stat agent release dates

```sh
cs lambda download-layers all us-west-2 latest agent
```

download a specific layer build

```sh
cs lambda download-layers NewRelicPython39 us-west-2 36
```

- If leaving off the optional compatibleRuntime, a list of compatible runtimes is obtained.
- If leaving off the optional region, the default region defined in your aws-cli is used.
- If leaving off the optional build, the latest build is downloaded.
- If leaving off the optional extension or agent, details for both will be displayed.

*Tested on Ubuntu 22.04 with Bash version 5.1.16*

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md)
