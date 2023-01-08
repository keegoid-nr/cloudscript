# CloudScript

A script for managing states of EC2 instances on AWS.

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
./cs lambda list-layers (compatibleRuntime|all) (region) | download-layers (layer|all) (region) (build)"
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
    cs lambda list-layers (compatibleRuntime|all) (region) | download-layers (layer|all) (region) (build)
    ```

For EKS:

- If leaving off the optional instanceId a list of instanceIds will be shown, but only those with a `Name` tag.

  ```json
            "Tags": [
              {
                "Key": "Name",
                "Value": "your-ec2-instance-name"
              }
            ],
  ```

For Lambda:

- If leaving off the optional compatibleRuntime, a list of compatible runtimes is obtained.
- If leaving off the optional region, the default region defined in your aws-cli is used.
- If leaving off the optional build, the latest build is downloaded.

*Tested on Ubuntu 22.04 with Bash version 5.1.16*
