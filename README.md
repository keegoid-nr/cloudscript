# CloudScript

A script for managing states of EC2 instances on AWS.

## Requirements

AWS CLI v2 must be installed and configured, like by running `aws configure`.  
https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2.html

jq must be installed.  
https://stedolan.github.io/jq/

eksctl must be installed for use with EKS.  
https://eksctl.io/

## Usage

```sh
# EC2
./cs start|stop|restart|status|dns|user [instanceId]

# EKS
./cs eks start|stop|status [cluster] [nodes]
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
    ```

If leaving off the optional instanceId a list of instanceIds will be shown, but only those with a `Name` tag.

```json
          "Tags": [
            {
              "Key": "Name",
              "Value": "your-ec2-instance-name"
            }
          ],
```

*Note: tested on Ubuntu 21.04 with Bash version 5.1.8*
