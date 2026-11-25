import { Component } from '@angular/core';

@Component({
  selector: 'app-home',
  template: `
    <section class="card welcome">
      <h1>Welcome to ClaimDesk</h1>
      <p>Choose who you are from the <strong>Signed in as</strong> menu at the top right.</p>
      <ul>
        <li><strong>Claimants</strong> report incidents, track claims and answer questions.</li>
        <li><strong>Claims officers</strong> pick up, assess, settle or reject claims.</li>
        <li><strong>Managers</strong> see workload and liability exposure.</li>
      </ul>
      <p class="muted">This is a local demo. Choosing a user stands in for signing in.</p>
    </section>
  `,
})
export class Home {}
