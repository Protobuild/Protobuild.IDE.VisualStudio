#!/usr/bin/env groovy
stage("Windows") {
  node('windows') {
    checkout poll: false, changelog: false, scm: scm
    bat ("Protobuild.exe --upgrade-all")
    bat ('Protobuild.exe --automated-build')
  }
}