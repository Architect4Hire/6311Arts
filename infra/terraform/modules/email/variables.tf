variable "names" {
  type = map(string)
}

variable "resource_group_name" {
  type = string
}

variable "tags" {
  type = map(string)
}

variable "data_location" {
  type = string
}

variable "custom_domain" {
  type    = string
  default = null
}
