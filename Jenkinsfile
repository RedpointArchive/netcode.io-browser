stage('Build') {
  node('windows') {
    checkout scm
    powershell '.\\build\\Build-Win32.ps1'
    stash includes: 'netcode.io.demoserver/**', name: 'demoserver'
    archiveArtifacts 'output/**'
  }
}
stage('Build Docker') {
  node('linux-docker') {
    unstash 'demoserver'
    sh 'cd netcode.io.demoserver && docker build . -t redpointgames/netcode-demo-server:latest'
    sh 'docker push redpointgames/netcode-demo-server:latest'
  }
}