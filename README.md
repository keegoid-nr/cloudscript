# CloudScript

<!-- START doctoc generated TOC please keep comment here to allow auto update -->
<!-- DON'T EDIT THIS SECTION, INSTEAD RE-RUN doctoc TO UPDATE -->
**Table of Contents**  *generated with [DocToc](https://github.com/thlorenz/doctoc)*

- [Requirements](#requirements)
- [Usage](#usage)
  - [EKS](#eks)
  - [Lambda](#lambda)

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

```sh
# cs command [required] (optional)

# EC2
./cs start|stop|restart|status|dns [instanceId]

# EKS
./cs eks start|stop|status [cluster] [nodes]

# Lambda
./cs lambda list-layers (compatibleRuntime|all) (region) | download-layers (layer|all) (region) (build#|latest) (extension|agent)"
```

1. Copy `cs` to your PATH.
1. Verify it is showing up with:

    ```sh
    which cs
    ```

1. Then run with:

    ```sh
    cs start|stop|restart|status|dns|user [instanceId]
    cs eks start|stop|status [cluster] [nodes]
    cs lambda list-layers (compatibleRuntime|all) (region) | download-layers (layer|all) (region) (build#|latest) (extension|agent)
    ```

### EC2

get a list of instances and their IDs

```sh
cs start
```

start an instance

```sh
cs start <your-instance-id>
```

stop an instance

```sh
cs eks stop <your-instance-id>
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
