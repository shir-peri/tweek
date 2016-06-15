import '../modules/styles.css'
import React from 'react'
import { Route, IndexRoute, Redirect } from 'react-router'
import App from './components/App'
import Home from './components/Home'
import KeysPage from './pages/keys/KeysPage';
import KeyPage from './pages/keys/KeyPage';
import NoMatch from './components/NoMatch'

export default serverRoutes=>(
  <Route>
    <Route path="/" component={App}>
      <IndexRoute component={Home}/>
      <Route path="keys" component={KeysPage} >
          <IndexRoute component={KeyPage} />
          <Route path="*" component={KeyPage}/>
      </Route>
    </Route>
    {serverRoutes}
    <Route path="*" status={404} component={NoMatch}/>
  </Route>
)